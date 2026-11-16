// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve storage verify &lt;store&gt;</c> (ADR 0183): the object-store
/// contract suite against a container or a bucket, as the caller's identity
/// (ADR 0137). It works in a scratch prefix it removes, leaves the
/// verification record at the store's root when the store passed, and with
/// <c>--record</c> writes the result record a pull request adds to
/// <c>testdata/object-stores/results/</c>. Exit code 0 is a pass, 1 a
/// counterexample.
/// </summary>
internal static class StorageVerify
{
    internal static Command Command(Io io)
    {
        Command verify = new("verify", "Run the object-store contract suite against a container or a bucket: the five operations and both races. A pass leaves a verification record at the store's root (ADR 0183).");
        Argument<string> store = new("store")
        {
            Description = "A container or a bucket, with no dataset after it: https://{account}.blob.core.windows.net/{container} or s3://{bucket}.",
        };
        Option<string?> endpoint = Storage.Endpoint();
        Option<string?> region = Storage.Region();
        Option<string?> sts = Storage.Sts();
        Option<int> rounds = new("--rounds") { Description = "How many times each race is run, sixteen clients a round.", DefaultValueFactory = _ => VerificationRounds.Default.Value };
        Option<string?> record = new("--record") { Description = "Write the result record to this file, for testdata/object-stores/results/. Needs --provider, --server-version, --deployment and --signed-off-by." };
        Option<string?> provider = new("--provider") { Description = "--record: the provider's id on the compatibility page, as testdata/object-stores/providers.yaml has it." };
        Option<string?> version = new("--server-version") { Description = "--record: the server's version, or the service's API version: your statement, since the store does not say." };
        Option<string?> deployment = new("--deployment") { Description = "--record: where it ran: a region, or how the server was deployed." };
        Option<string> runBy = new("--run-by") { Description = "--record: who ran it: ci or maintainer.", DefaultValueFactory = _ => "maintainer" };
        Option<string?> signedOff = new("--signed-off-by") { Description = "--record: who vouches for the record: Name <email>." };
        verify.Arguments.Add(store);
        verify.Options.Add(endpoint);
        verify.Options.Add(region);
        verify.Options.Add(sts);
        verify.Options.Add(rounds);
        verify.Options.Add(record);
        verify.Options.Add(provider);
        verify.Options.Add(version);
        verify.Options.Add(deployment);
        verify.Options.Add(runBy);
        verify.Options.Add(signedOff);
        verify.SetAction((parsed, cancellationToken) => Cli.GuardAsync(
            async () =>
            {
                ResultRecord? result = Result(parsed.GetValue(record), parsed.GetValue(provider), parsed.GetValue(version), parsed.GetValue(deployment), parsed.GetValue(runBy)!, parsed.GetValue(signedOff));
                int count = parsed.GetValue(rounds);

                if (count < 1)
                {
                    throw new Cli.CommandException("--rounds is 1 or more.");
                }

                string address = parsed.GetValue(store)!;

                if (!ObjectLease.IsAddress(address))
                {
                    throw new Cli.CommandException("varve storage verify is for an object store: https://{account}.blob.core.windows.net/{container} or s3://{bucket}.");
                }

                IObjectStore opened = ObjectLease.OpenRoot(address, parsed.GetValue(endpoint), parsed.GetValue(region), parsed.GetValue(sts));

                using (opened as IDisposable)
                {
                    VerificationOptions options = new() { Rounds = new VerificationRounds(count), Randomness = SystemProbeRandomness.Instance, Clock = TimeProvider.System };
                    return await RunAsync(opened, address, options, result, io, cancellationToken).ConfigureAwait(false);
                }
            },
            parsed.InvocationConfiguration.Error));
        return verify;
    }

    /// <summary>What <c>--record</c> and its options ask for.</summary>
    internal sealed record ResultRecord(string File, string Provider, string ServerVersion, string Deployment, string RunBy, string SignedOffBy);

    /// <summary>
    /// The run itself, over a store already opened: the suite, what it found
    /// line by line, and the result record. The command's tests give it the
    /// stores no address reaches.
    /// </summary>
    internal static async Task<int> RunAsync(IObjectStore store, string shown, VerificationOptions options, ResultRecord? result, Io io, CancellationToken cancellationToken)
    {
        await io.Out.WriteLineAsync("store: " + shown).ConfigureAwait(false);
        await io.Out.WriteLineAsync("suite: " + ObjectStoreVerification.Suite + ", " + ObjectStoreVerification.Racers + " racers, " + options.Rounds + " rounds of each race").ConfigureAwait(false);
        VerificationOutcome outcome = await ObjectStoreVerification.RunAsync(store, options, cancellationToken).ConfigureAwait(false);

        foreach (VerificationCaseName passed in outcome.Passed)
        {
            await io.Out.WriteLineAsync("pass  " + passed).ConfigureAwait(false);
        }

        if (outcome.Counterexample is { } broken)
        {
            await io.Out.WriteLineAsync("FAIL  " + broken.Case).ConfigureAwait(false);
            await io.Out.WriteLineAsync("      " + broken.Violation).ConfigureAwait(false);
        }

        foreach (ObjectName left in outcome.LeftBehind)
        {
            await io.Error.WriteLineAsync("varve: could not remove the scratch object '" + left + "'; nothing reads it again, and it may be deleted by hand.").ConfigureAwait(false);
        }

        if (result is not null)
        {
            await File.WriteAllTextAsync(result.File, Render(result, outcome, options.Clock.GetUtcNow()), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
            await io.Out.WriteLineAsync("result record: " + result.File).ConfigureAwait(false);
        }

        if (outcome.Verified)
        {
            await io.Out.WriteLineAsync("verified: the store kept the contract. The verification record '" + ObjectStoreVerification.RecordName + "' is at its root.").ConfigureAwait(false);
            return 0;
        }

        await io.Error.WriteLineAsync("varve: not verified: the store broke the object-store contract (ADR 0134), so a log in it can be forked without either writer being told. Any verification record at its root was removed. docs/operator/object-stores.md lists the stores that pass.").ConfigureAwait(false);
        return 1;
    }

    /// <summary>
    /// The result record's text (ADR 0183): flat <c>key: "value"</c> lines,
    /// which <c>eng/object-stores.cs</c> reads. Its format is 1.
    /// </summary>
    internal static string Render(ResultRecord result, VerificationOutcome outcome, DateTimeOffset now)
    {
        StringBuilder text = new();
        text.Append("format: 1\n");
        Line(text, "provider", result.Provider);
        Line(text, "server-version", result.ServerVersion);
        Line(text, "deployment", result.Deployment);
        text.Append("suite: ").Append(outcome.Suite).Append('\n');
        Line(text, "date", now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        text.Append("racers: ").Append(ObjectStoreVerification.Racers).Append('\n');
        text.Append("rounds: ").Append(outcome.Rounds).Append('\n');
        text.Append("result: ").Append(outcome.Verified ? "pass" : "fail").Append('\n');

        if (outcome.Counterexample is { } broken)
        {
            Line(text, "counterexample-case", broken.Case.Value);
            Line(text, "counterexample", broken.Violation.Value);
        }

        Line(text, "run-by", result.RunBy);
        Line(text, "signed-off-by", result.SignedOffBy);
        return text.ToString();
    }

    private static void Line(StringBuilder text, string key, string value) =>
        text.Append(key).Append(": \"").Append(value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append("\"\n");

    private static ResultRecord? Result(string? file, string? provider, string? version, string? deployment, string runBy, string? signedOff)
    {
        if (file is null)
        {
            if (provider is not null || version is not null || deployment is not null || signedOff is not null)
            {
                throw new Cli.CommandException("--provider, --server-version, --deployment and --signed-off-by describe a result record: give --record <file> with them.");
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(deployment) || string.IsNullOrWhiteSpace(signedOff))
        {
            throw new Cli.CommandException("--record needs --provider, --server-version, --deployment and --signed-off-by: the store does not say what it is, so the record carries your statement.");
        }

        foreach (char c in provider)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                throw new Cli.CommandException("--provider is an id from testdata/object-stores/providers.yaml: lower-case letters, digits and hyphens.");
            }
        }

        if (runBy is not ("ci" or "maintainer"))
        {
            throw new Cli.CommandException("--run-by is ci or maintainer.");
        }

        foreach (string value in (string[])[version, deployment, signedOff])
        {
            if (value.AsSpan().ContainsAny('\n', '\r'))
            {
                throw new Cli.CommandException("a result record's values are one line each.");
            }
        }

        return new ResultRecord(file, provider, version, deployment, runBy, signedOff);
    }
}
