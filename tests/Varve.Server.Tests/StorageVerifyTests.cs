// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Server.Commands;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// <c>varve storage verify</c> (ADR 0183, #117): it passes a store that keeps
/// the contract and fails, with the counterexample, the two that do not; its
/// result record is what <c>eng/object-stores.cs</c> reads; and it refuses an
/// address that is not a store's root.
/// </summary>
public sealed class StorageVerifyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task a_store_that_keeps_the_contract_passes_and_its_result_record_says_so()
    {
        ScratchStore store = new(Breaks.Nothing);
        string file = Path.Combine(Directory.CreateTempSubdirectory("varve-verify-").FullName, "record.yaml");
        StorageVerify.ResultRecord record = new(file, "example-store", "1.2.3", "a test, \"in memory\"", "ci", "A Tester <tester@example.org>");

        (int exit, string output, string error) = await RunAsync(store, record);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(
            """
            store: memory://scratch
            suite: 1, 16 racers, 8 rounds of each race
            pass  create-if-absent writes once and reads back
            pass  replace-if-unchanged needs the current version
            pass  a ranged read returns the range, short at the end, none for no object
            pass  a list returns the prefix's objects in ordinal order with their lengths
            pass  a delete deletes once
            pass  of clients racing create-if-absent on one name exactly one wins
            pass  of clients racing replace-if-unchanged on one version exactly one wins

            """.ReplaceLineEndings("\n") + "result record: " + file + "\nverified: the store kept the contract in every case, with no counterexample in 8 rounds of each race. That is evidence, not a proof. The verification record 'varve-verification' is at its root. Run this again after upgrading the store.\n",
            output);
        // Format 1 of the result record, as testdata/formats/verification-next holds it (ADR 0216).
        Assert.Equal(await FixtureAsync("result-pass.yaml", file), await File.ReadAllTextAsync(file, Ct));
        Assert.Equal(["varve-verification"], store.Names());
    }

    /// <summary>The gate's first broken store: one that ignores conditions, as Garage does.</summary>
    [Fact]
    public async Task a_store_that_ignores_conditions_fails_with_the_counterexample()
    {
        ScratchStore store = new(Breaks.IgnoresConditions);
        string file = Path.Combine(Directory.CreateTempSubdirectory("varve-verify-").FullName, "record.yaml");

        (int exit, string output, string error) = await RunAsync(store, new StorageVerify.ResultRecord(file, "broken", "0", "a test", "maintainer", "A Tester <tester@example.org>"));

        Assert.Equal(1, exit);
        Assert.Contains("FAIL  create-if-absent writes once and reads back\n      a second create of the same name returned a version\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain("pass  ", output, StringComparison.Ordinal);
        Assert.DoesNotContain("verified:", output, StringComparison.Ordinal);
        Assert.Contains("not verified: the store broke the object-store contract", error, StringComparison.Ordinal);
        Assert.Contains("docs/operator/object-stores.md", error, StringComparison.Ordinal);

        Assert.Equal(await FixtureAsync("result-fail.yaml", file), await File.ReadAllTextAsync(file, Ct));
        Assert.Empty(store.Names());
    }

    /// <summary>
    /// The gate's second: one that enforces each condition a request at a
    /// time and loses races, which the open's probe cannot see (ADR 0134).
    /// The five operations pass and the race is the counterexample.
    /// </summary>
    [Fact]
    public async Task a_store_that_loses_races_fails_the_race_with_the_counterexample()
    {
        ScratchStore store = new(Breaks.LosesRaces);

        (int exit, string output, string error) = await RunAsync(store, null);

        Assert.Equal(1, exit);
        Assert.Equal(5, output.Split('\n').Count(line => line.StartsWith("pass  ", StringComparison.Ordinal)));
        Assert.Contains("FAIL  of clients racing create-if-absent on one name exactly one wins\n      round 0: ", output, StringComparison.Ordinal);
        Assert.Contains(" of 16 racing creates were told they wrote\n", output, StringComparison.Ordinal);
        Assert.Contains("not verified", error, StringComparison.Ordinal);
        Assert.Empty(store.Names());
    }

    [Theory]
    [InlineData("a container or a bucket, with no dataset after it", "storage", "verify", "https://account.blob.core.windows.net/container/dataset")]
    [InlineData("a container or a bucket, with no dataset after it", "storage", "verify", "https://account.blob.core.windows.net/")]
    [InlineData("a container or a bucket, with no dataset after it", "storage", "verify", "s3://bucket/dataset", "--endpoint", "http://127.0.0.1:9", "--region", "us-east-1")]
    [InlineData("needs --endpoint and --region", "storage", "verify", "s3://bucket")]
    [InlineData("is for an object store", "storage", "verify", "/var/lib/varve/orders")]
    [InlineData("--rounds is 1 or more", "storage", "verify", "https://account.blob.core.windows.net/container", "--rounds", "0")]
    [InlineData("--record needs --provider, --server-version, --deployment and --signed-off-by", "storage", "verify", "https://account.blob.core.windows.net/container", "--record", "r.yaml", "--provider", "x")]
    [InlineData("give --record <file> with them", "storage", "verify", "https://account.blob.core.windows.net/container", "--provider", "x")]
    [InlineData("lower-case letters, digits and hyphens", "storage", "verify", "https://account.blob.core.windows.net/container", "--record", "r.yaml", "--provider", "Azure Blob", "--server-version", "1", "--deployment", "d", "--signed-off-by", "s")]
    [InlineData("--run-by is ci or maintainer", "storage", "verify", "https://account.blob.core.windows.net/container", "--record", "r.yaml", "--provider", "x", "--server-version", "1", "--deployment", "d", "--signed-off-by", "s", "--run-by", "me")]
    public async Task what_is_not_a_run_is_refused_before_the_store_is_touched(string says, params string[] args)
    {
        StringWriter output = new() { NewLine = "\n" };
        StringWriter error = new() { NewLine = "\n" };

        int exit = await Cli.RunAsync(args, new Io(output, error, new MemoryStream()), Ct);

        Assert.Equal(1, exit);
        Assert.Contains(says, error.ToString(), StringComparison.Ordinal);
    }

    // A file of the fixture, its sum checked first; written from what the run wrote when the variable asks.
    private static async Task<string> FixtureAsync(string name, string written)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Varve.slnx")))
        {
            root = root.Parent;
        }

        string fixture = Path.Combine(root?.FullName ?? throw new DirectoryNotFoundException("No repository root above " + AppContext.BaseDirectory + "."), "testdata", "formats", "verification-next");

        if (Environment.GetEnvironmentVariable("VARVE_WRITE_VERIFICATION_FIXTURE") is { Length: > 0 } write)
        {
            File.Copy(written, Path.Combine(write, "verification-next", name), overwrite: true);
        }

        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(fixture, name), Ct);
        string sum = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        Assert.Contains(sum + "  ./" + name, await File.ReadAllLinesAsync(Path.Combine(fixture, "SHA256SUMS"), Ct));
        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task<(int Exit, string Out, string Error)> RunAsync(ScratchStore store, StorageVerify.ResultRecord? record)
    {
        StringWriter output = new() { NewLine = "\n" };
        StringWriter error = new() { NewLine = "\n" };
        VerificationOptions options = new() { Randomness = SystemProbeRandomness.Instance, Clock = new FixedClock(Now) };
        int exit = await StorageVerify.RunAsync(store, store.ToString(), options, record, new Io(output, error, new MemoryStream()), Ct);
        return (exit, output.ToString(), error.ToString());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private enum Breaks
    {
        Nothing,
        IgnoresConditions,
        LosesRaces,
    }

    /// <summary>
    /// An object store in memory for the command: correct, or broken in one
    /// of the gate's two ways. The server's tests cannot reach the store
    /// tests' own, and need none of its hooks.
    /// </summary>
    private sealed class ScratchStore(Breaks breaks) : IObjectStore
    {
        private readonly Lock _gate = new();
        private readonly SortedDictionary<string, (byte[] Bytes, long Version)> _objects = new(StringComparer.Ordinal);
        private long _versions;

        public IReadOnlyList<string> Names()
        {
            lock (_gate)
            {
                return [.. _objects.Keys];
            }
        }

        public override string ToString() => "memory://scratch";

        public async ValueTask<ObjectVersion?> CreateIfAbsentAsync(ObjectName name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_objects.ContainsKey(name.Value) && breaks != Breaks.IgnoresConditions)
                {
                    return null;
                }

                if (breaks != Breaks.LosesRaces)
                {
                    return Write(name, content);
                }
            }

            // Checked, and written a moment later without checking again.
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);

            lock (_gate)
            {
                return Write(name, content);
            }
        }

        public async ValueTask<ObjectVersion?> ReplaceIfUnchangedAsync(ObjectName name, ObjectVersion expected, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!_objects.TryGetValue(name.Value, out var found) || (Version(found.Version) != expected && breaks != Breaks.IgnoresConditions))
                {
                    return null;
                }

                if (breaks != Breaks.LosesRaces)
                {
                    return Write(name, content);
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);

            lock (_gate)
            {
                return Write(name, content);
            }
        }

        public ValueTask<ObjectRead?> ReadAsync(ObjectName name, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!_objects.TryGetValue(name.Value, out var found))
                {
                    return new ValueTask<ObjectRead?>((ObjectRead?)null);
                }

                long start = Math.Min(offset.Value, found.Bytes.Length);
                int count = (int)Math.Min(found.Bytes.Length - start, length.Value);
                return new ValueTask<ObjectRead?>(new ObjectRead(found.Bytes.AsSpan((int)start, count).ToArray(), Version(found.Version), new ByteCount(found.Bytes.Length)));
            }
        }

        public ValueTask<IReadOnlyList<ObjectEntry>> ListAsync(ObjectName prefix, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return new ValueTask<IReadOnlyList<ObjectEntry>>([.. _objects
                    .Where(pair => pair.Key.StartsWith(prefix.Value, StringComparison.Ordinal))
                    .Select(pair => new ObjectEntry(new ObjectName(pair.Key), new ByteCount(pair.Value.Bytes.Length), Version(pair.Value.Version)))]);
            }
        }

        public ValueTask<bool> DeleteAsync(ObjectName name, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return new ValueTask<bool>(_objects.Remove(name.Value));
            }
        }

        private static ObjectVersion Version(long version) => new("v" + version.ToString(CultureInfo.InvariantCulture));

        private ObjectVersion Write(ObjectName name, ReadOnlyMemory<byte> content)
        {
            _objects[name.Value] = (content.ToArray(), ++_versions);
            return Version(_versions);
        }
    }
}

/// <summary>
/// The command against the stores CI starts (the gate of #117): Azurite, and
/// RustFS reached by web identity, each in a fresh container or bucket. The
/// run's result record goes to <c>VARVE_VERIFY_RECORDS</c> when that names a
/// directory, which is how CI's records are made.
/// </summary>
public sealed class StorageVerifyServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public Task varve_storage_verify_passes_against_azurite() => VerifiesAsync(FleetStore.Azurite, "azurite", "3.37.0");

    [Fact]
    public Task varve_storage_verify_passes_against_rustfs_by_web_identity() => VerifiesAsync(FleetStore.RustFs, "rustfs", "1.0.1");

    private static async Task VerifiesAsync(FleetStore store, string provider, string version)
    {
        FleetLocation location = await store.NewAsync();
        string directory = Environment.GetEnvironmentVariable("VARVE_VERIFY_RECORDS") is { Length: > 0 } records
            ? Directory.CreateDirectory(records).FullName
            : Directory.CreateTempSubdirectory("varve-verify-").FullName;
        string file = Path.Combine(directory, provider + "-" + version + ".yaml");
        string rounds = Environment.GetEnvironmentVariable("VARVE_VERIFY_ROUNDS") is { Length: > 0 } set ? set : "8";
        StringWriter output = new() { NewLine = "\n" };
        StringWriter error = new() { NewLine = "\n" };

        int exit = await Cli.RunAsync(
            [
                .. location.Verify(), "--rounds", rounds, "--record", file, "--provider", provider, "--server-version", version,
                "--deployment", "CI, .github/storage/compose.yaml, one container on loopback", "--run-by", "ci",
                "--signed-off-by", "the storage (services) job, .github/workflows/ci.yml",
            ],
            new Io(output, error, new MemoryStream()),
            Ct);

        Assert.True(exit == 0, output.ToString() + error);
        Assert.Contains("verified: the store kept the contract in every case", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("result: pass\n", await File.ReadAllTextAsync(file, Ct), StringComparison.Ordinal);

        // The record a shipper will read is there, and a second run replaces it.
        Assert.Equal(0, await Cli.RunAsync([.. location.Verify(), "--rounds", "1"], new Io(output, error, new MemoryStream()), Ct));
        Console.WriteLine(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file, Ct)));
    }
}
