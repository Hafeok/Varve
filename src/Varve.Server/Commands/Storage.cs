// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve storage &lt;address&gt;</c> (ADR 0162): a dataset in an object store
/// as its objects show it — the catalog's extent sets per permutation, the
/// sets no catalog names that the store still keeps, and what each reader
/// record holds of them — read without changing anything; and
/// <c>varve storage readers list</c> and <c>forget</c>, the operator's events
/// for a reader that is gone (ADR 0160). The store is reached as the caller's
/// identity (ADR 0137): <c>forget</c> needs write access to <c>readers/</c>,
/// which is its authorisation.
/// </summary>
internal static class Storage
{
    internal static Command Command(Io io)
    {
        Command command = new("storage", "A dataset in an object store: its extent sets, the bytes kept for readers, and its reader records (ADRs 0160, 0162).");
        Argument<string> address = Address();
        Option<string?> endpoint = Endpoint();
        Option<string?> region = Region();
        command.Arguments.Add(address);
        command.Options.Add(endpoint);
        command.Options.Add(region);
        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(
            () => ReportAsync(parsed.GetValue(address)!, parsed.GetValue(endpoint), parsed.GetValue(region), io, cancellationToken),
            parsed.InvocationConfiguration.Error));
        command.Subcommands.Add(Readers(io));
        return command;
    }

    private static Command Readers(Io io)
    {
        Command readers = new("readers", "The reader records: every instance that reads the dataset's extents announces the catalog versions it holds (ADR 0160).");

        Command list = new("list", "Every reader record, the versions it announces, and the bytes of unnamed sets it holds back from deletion.");
        Argument<string> listAddress = Address();
        Option<string?> listEndpoint = Endpoint();
        Option<string?> listRegion = Region();
        list.Arguments.Add(listAddress);
        list.Options.Add(listEndpoint);
        list.Options.Add(listRegion);
        list.SetAction((parsed, cancellationToken) => Cli.GuardAsync(
            () => ListAsync(parsed.GetValue(listAddress)!, parsed.GetValue(listEndpoint), parsed.GetValue(listRegion), io, cancellationToken),
            parsed.InvocationConfiguration.Error));

        Command forget = new("forget", "Forget a reader that is gone, so the writer may delete what its record held. Only while the record still announces what --oldest and --newest say, as list showed it.");
        Argument<string> forgetAddress = Address();
        Option<string?> forgetEndpoint = Endpoint();
        Option<string?> forgetRegion = Region();
        Option<string> reader = new("--reader") { Description = "The reader id, as list shows it.", Required = true };
        Option<long> oldest = new("--oldest") { Description = "The oldest catalog version the record announces, as list shows it.", Required = true };
        Option<long> newest = new("--newest") { Description = "The newest catalog version the record announces, as list shows it.", Required = true };
        forget.Arguments.Add(forgetAddress);
        forget.Options.Add(forgetEndpoint);
        forget.Options.Add(forgetRegion);
        forget.Options.Add(reader);
        forget.Options.Add(oldest);
        forget.Options.Add(newest);
        forget.SetAction((parsed, cancellationToken) => Cli.GuardAsync(
            () => ForgetAsync(
                parsed.GetValue(forgetAddress)!, parsed.GetValue(forgetEndpoint), parsed.GetValue(forgetRegion),
                parsed.GetValue(reader)!, parsed.GetValue(oldest), parsed.GetValue(newest), io, cancellationToken),
            parsed.InvocationConfiguration.Error));

        readers.Subcommands.Add(list);
        readers.Subcommands.Add(forget);
        return readers;
    }

    private static Argument<string> Address() => new("dataset")
    {
        Description = "A dataset in an object store: https://{account}.blob.core.windows.net/{container}/{dataset} or s3://{bucket}/{dataset}.",
    };

    private static Option<string?> Endpoint() => new("--endpoint") { Description = "s3://: the S3-compatible service's endpoint." };

    private static Option<string?> Region() => new("--region") { Description = "s3://: the region the signature is for." };

    [DecisionDriven.DesignDecision(typeof(DecisionDriven.Ledger.Varve.StorageOperationsAndTheReadersCommand.StorageReportsSetsAndRetention), Scope = DecisionDriven.ExceptionScope.Boundary)]
    private static async Task<int> ReportAsync(string address, string? endpoint, string? region, Io io, CancellationToken cancellationToken)
    {
        (IObjectStore store, ObjectName prefix) = Open(address, endpoint, region);

        using (store as IDisposable)
        {
            StorageReport report = await Backend.ReadReportAsync(store, prefix, cancellationToken).ConfigureAwait(false);

            if (report.Catalog is not { } catalog)
            {
                await io.Out.WriteLineAsync("catalog: none (no checkpoint yet)").ConfigureAwait(false);
            }
            else
            {
                await io.Out.WriteLineAsync("catalog: version " + catalog.Version + ", position " + catalog.Position + ", dataset " + catalog.Dataset).ConfigureAwait(false);
            }

            // A set's six order extents share its window, so every permutation has as many.
            await io.Out.WriteLineAsync("extent sets per permutation: " + Number(report.Sets.Count)).ConfigureAwait(false);
            long[] perOrder = new long[6];
            long rest = 0;

            foreach (StoredSet set in report.Sets)
            {
                for (int o = 0; o < perOrder.Length; o++)
                {
                    perOrder[o] += set.Orders[o].Value;
                }

                rest += set.Rest.Value;
            }

            string[] orders = ["SPOG", "POSG", "OSPG", "GSPO", "GPOS", "GOSP"];

            for (int o = 0; o < orders.Length; o++)
            {
                await io.Out.WriteLineAsync("  " + orders[o] + ": " + Number(report.Sets.Count) + " extents, " + Bytes(perOrder[o])).ConfigureAwait(false);
            }

            await io.Out.WriteLineAsync("  dictionaries, graph-version and commit indexes: " + Bytes(rest)).ConfigureAwait(false);
            await io.Out.WriteLineAsync("sets:").ConfigureAwait(false);

            foreach (StoredSet set in report.Sets)
            {
                long total = set.Rest.Value;

                foreach (ByteCount order in set.Orders)
                {
                    total += order.Value;
                }

                await io.Out.WriteLineAsync("  " + set.Set.Name + "  (" + set.Set.From + ", " + set.Set.To + "]  " + Bytes(total)).ConfigureAwait(false);
            }

            await io.Out.WriteLineAsync("retained: " + Number(report.UnnamedSets.Count) + " sets no catalog names, " + Bytes(report.RetainedBytes.Value)).ConfigureAwait(false);
            await io.Out.WriteLineAsync("readers: " + Number(report.Readers.Count)).ConfigureAwait(false);

            foreach (ReaderHolding holding in report.Readers)
            {
                await io.Out.WriteLineAsync("  " + Line(holding)).ConfigureAwait(false);
            }

            return 0;
        }
    }

    private static async Task<int> ListAsync(string address, string? endpoint, string? region, Io io, CancellationToken cancellationToken)
    {
        (IObjectStore store, ObjectName prefix) = Open(address, endpoint, region);

        using (store as IDisposable)
        {
            StorageReport report = await Backend.ReadReportAsync(store, prefix, cancellationToken).ConfigureAwait(false);
            await io.Out.WriteLineAsync("catalog: " + (report.Catalog is { } catalog ? "version " + catalog.Version : "none")).ConfigureAwait(false);

            if (report.Readers.Count == 0)
            {
                await io.Out.WriteLineAsync("readers: none").ConfigureAwait(false);
                return 0;
            }

            foreach (ReaderHolding holding in report.Readers)
            {
                await io.Out.WriteLineAsync(Line(holding)).ConfigureAwait(false);
            }

            return 0;
        }
    }

    [DecisionDriven.DesignDecision(typeof(DecisionDriven.Ledger.Varve.ExtentDeletionByReaderAnnouncements.ADeadRecordHoldsUntilAnEvent), Scope = DecisionDriven.ExceptionScope.Boundary)]
    private static async Task<int> ForgetAsync(string address, string? endpoint, string? region, string reader, long oldest, long newest, Io io, CancellationToken cancellationToken)
    {
        if (!ReaderId.IsValid(reader))
        {
            throw new Cli.CommandException("'" + reader + "' is not a reader id; varve storage readers list shows them.");
        }

        if (oldest < 0 || newest < oldest)
        {
            throw new Cli.CommandException("--oldest and --newest are the record's interval, as varve storage readers list shows it: 0 <= oldest <= newest.");
        }

        (IObjectStore store, ObjectName prefix) = Open(address, endpoint, region);

        using (store as IDisposable)
        {
            bool forgotten = await Backend.ForgetReaderAsync(
                store, prefix, new ReaderId(reader), new CatalogVersion(oldest), new CatalogVersion(newest), TimeProvider.System, cancellationToken).ConfigureAwait(false);

            if (!forgotten)
            {
                await io.Error.WriteLineAsync("varve: the reader record '" + reader + "' is gone, or no longer announces " + oldest.ToString(CultureInfo.InvariantCulture) + " to " + newest.ToString(CultureInfo.InvariantCulture) + "; nothing was forgotten. It may be alive: list again.").ConfigureAwait(false);
                return 1;
            }

            await io.Out.WriteLineAsync("forgot reader " + reader + ": the writer may delete what it held at its next maintenance round. If that reader is still alive, its reads that find an extent gone fail with reader-forgotten, and it attaches again.").ConfigureAwait(false);
            return 0;
        }
    }

    private static (IObjectStore Store, ObjectName Prefix) Open(string address, string? endpoint, string? region)
    {
        if (!ObjectLease.IsAddress(address))
        {
            throw new Cli.CommandException("varve storage is for a dataset in an object store: https://{account}.blob.core.windows.net/{container}/{dataset} or s3://{bucket}/{dataset}.");
        }

        return ObjectLease.Open(address, endpoint, region);
    }

    private static string Line(ReaderHolding holding)
    {
        ReaderRecord record = holding.Record;
        string versions = record.Understood
            ? "versions " + record.Oldest + " to " + record.Newest
            : "a record of a format this build does not read: holds every version";
        return record.Reader
            + "  " + versions
            + "  " + (record.State == ReaderRecordState.Forgotten ? "forgotten" : "announced")
            + (record.Holder is { } holder ? "  by " + holder.Value : string.Empty)
            + (record.AnnouncedAt is { } at ? "  at " + at.Value.ToString("O", CultureInfo.InvariantCulture) : string.Empty)
            + "  retains " + Bytes(holding.RetainedBytes.Value);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Bytes(long value) => value.ToString("N0", CultureInfo.InvariantCulture) + " bytes";
}
