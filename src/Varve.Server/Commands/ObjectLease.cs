// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store;
using Varve.Store.Azure;
using Varve.Store.Azure.Model;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.S3;
using Varve.Store.S3.Model;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve lease</c> for a dataset in an object store (ADR 0136): the writer
/// record, read without changing anything; and with <c>--take --epoch n</c>,
/// the operator's takeover — epoch n+1 claimed only while the record still
/// shows n, the log fenced with it, and released for the next writer. No
/// time is consulted, and the claim is recorded with the trigger
/// <c>operator</c>. The store is reached as the caller's identity (ADR 0137).
/// </summary>
internal static class ObjectLease
{
    /// <summary>
    /// Whether a read command's dataset is in an object store rather than on
    /// a server: <c>s3://</c>, an Azure Blob Storage host, or Azurite's
    /// development account on a loopback address.
    /// </summary>
    internal static bool IsObjectStore(string dataset)
    {
        if (dataset.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(dataset, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        return uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase)
            || (uri.IsLoopback && uri.AbsolutePath.StartsWith("/devstoreaccount1/", StringComparison.Ordinal));
    }

    internal static bool IsAddress(string dataset) =>
        dataset.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || dataset.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || dataset.StartsWith("s3://", StringComparison.OrdinalIgnoreCase);

    internal static async Task<int> RunAsync(string address, bool breakIt, bool take, long? epoch, string? endpoint, string? region, Io io, CancellationToken cancellationToken)
    {
        if (breakIt)
        {
            throw new Cli.CommandException("a dataset in an object store has no lock to break; --take --epoch n takes its writer epoch over (ADR 0136).");
        }

        if (take && epoch is null)
        {
            throw new Cli.CommandException("--take needs --epoch: the epoch it takes over from, as varve lease shows it, so a takeover never supersedes a writer the operator did not see (ADR 0136).");
        }

        (IObjectStore store, ObjectName prefix) = Open(address, endpoint, region);

        using (store as IDisposable)
        {
            WriterRecord? record = await Backend.ReadWriterAsync(store, prefix, cancellationToken).ConfigureAwait(false);

            if (!take)
            {
                await PrintAsync(record, io).ConfigureAwait(false);
                return 0;
            }

            string cache = Directory.CreateTempSubdirectory("varve-lease-").FullName;

            try
            {
                WriterRecord? after = await Backend.TakeOverAsync(new ObjectStorageOptions
                {
                    Store = store,
                    Prefix = prefix,
                    Cache = new CacheDirectory(cache),
                    Instance = new WriterInstance("varve lease on " + Environment.MachineName),
                    Clock = TimeProvider.System,
                    ProbeRandomness = SystemProbeRandomness.Instance,
                }, new WriterEpoch(epoch!.Value), cancellationToken).ConfigureAwait(false);

                if (after is null)
                {
                    await io.Error.WriteLineAsync("varve: the writer record no longer shows epoch " + epoch.Value.ToString(CultureInfo.InvariantCulture) + "; nothing was taken.").ConfigureAwait(false);
                    await PrintAsync(await Backend.ReadWriterAsync(store, prefix, cancellationToken).ConfigureAwait(false), io).ConfigureAwait(false);
                    return 1;
                }

                await io.Out.WriteLineAsync("took epoch " + epoch.Value.ToString(CultureInfo.InvariantCulture) + " over: epoch " + after.Epoch + " claimed, the log fenced, and released for the next writer.").ConfigureAwait(false);
                await PrintAsync(after, io).ConfigureAwait(false);
                return 0;
            }
            finally
            {
                Directory.Delete(cache, recursive: true);
            }
        }
    }

    internal static (IObjectStore Store, ObjectName Prefix) Open(string address, string? endpoint, string? region)
    {
        Uri uri = new(address);
        string[] parts = uri.AbsolutePath.Trim('/').Split('/', 2);

        if (uri.Scheme == "s3")
        {
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(region) || parts.Length < 1 || parts[0].Length == 0)
            {
                throw new Cli.CommandException("s3://{bucket}/{dataset} needs --endpoint and --region.");
            }

            string dataset = uri.Host.Length > 0 ? uri.AbsolutePath.Trim('/') : (parts.Length > 1 ? parts[1] : string.Empty);
            string bucket = uri.Host.Length > 0 ? uri.Host : parts[0];

            if (dataset.Length == 0)
            {
                throw new Cli.CommandException("s3://{bucket}/{dataset} names the dataset after the bucket.");
            }

            return (S3ObjectStore.Create(new S3ObjectStoreOptions
            {
                Endpoint = new Uri(endpoint),
                Bucket = new S3Bucket(bucket),
                Region = new S3Region(region),
                Clock = TimeProvider.System,
            }), new ObjectName(dataset + "/"));
        }

        // An Azurite address carries the account in its path: http://127.0.0.1:10000/{account}/{container}/{dataset}.
        bool loopback = uri.IsLoopback;
        string[] segments = uri.AbsolutePath.Trim('/').Split('/');
        int containerAt = loopback ? 1 : 0;

        if (segments.Length < containerAt + 2)
        {
            throw new Cli.CommandException("'" + address + "' names no dataset: https://{account}.blob.core.windows.net/{container}/{dataset}.");
        }

        Uri container = new(uri.GetLeftPart(UriPartial.Authority) + "/" + string.Join('/', segments[..(containerAt + 1)]));
        return (AzureBlobStore.Create(new AzureBlobStoreOptions { ContainerUrl = container, Clock = TimeProvider.System }), new ObjectName(string.Join('/', segments[(containerAt + 1)..]) + "/"));
    }

    private static async Task PrintAsync(WriterRecord? record, Io io)
    {
        if (record is null)
        {
            await io.Out.WriteLineAsync("epoch: none claimed").ConfigureAwait(false);
            return;
        }

        await io.Out.WriteLineAsync("epoch: " + record.Epoch).ConfigureAwait(false);
        await io.Out.WriteLineAsync("holder: " + record.Holder.Value).ConfigureAwait(false);
        await io.Out.WriteLineAsync("released: " + (record.Released?.ToString() ?? "no")).ConfigureAwait(false);
        await io.Out.WriteLineAsync("history:").ConfigureAwait(false);

        foreach (EpochClaim claim in record.History)
        {
            await io.Out.WriteLineAsync(
                "  epoch " + claim.Epoch
                + "  " + claim.Trigger.ToString().ToLowerInvariant()
                + "  by " + claim.Holder.Value
                + "  at " + claim.ClaimedAt.Value.ToString("O", CultureInfo.InvariantCulture)
                + (claim.Fence is { } fence ? "  fence " + fence.Value : string.Empty)
                + (claim.Released is { } released ? "  released: " + released.ToString().ToLowerInvariant() : string.Empty)).ConfigureAwait(false);
        }
    }
}
