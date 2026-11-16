// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The shipped contract suite as a run (ADR 0183): its verdict against a
/// store that keeps the contract and against the two that do not, its
/// scratch prefix, and the verification record it leaves at the store's root.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class ObjectStoreVerificationTests
{
    private const string Record = "varve-verification";

    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    internal static VerificationOptions Options(int? rounds = null) => new()
    {
        Rounds = new VerificationRounds(rounds ?? ObjectStoreContractCases.Rounds),
        Randomness = TestRandomness.Instance,
        Clock = new FixedClock(Now),
    };

    [Fact]
    public async Task a_store_that_keeps_the_contract_passes_and_is_left_with_the_record_alone()
    {
        MemoryObjectStore store = new();
        store.Put("orders/log/MANIFEST", [1]);

        VerificationOutcome outcome = await ObjectStoreVerification.RunAsync(store, Options(), T.Ct);

        Assert.True(outcome.Verified);
        Assert.Null(outcome.Counterexample);
        Assert.Equal(ObjectStoreVerification.Cases().Select(c => c.Name), outcome.Passed.Select(c => c.Value));
        Assert.Empty(outcome.LeftBehind);
        Assert.Equal(ObjectStoreVerification.Suite, outcome.Suite);

        // Nothing of the run is left but the record, and what was there is untouched.
        Assert.Equal(["orders/log/MANIFEST", Record], store.Names());
        Assert.Equal([1], store.Get("orders/log/MANIFEST"));

        VerificationRecord record = Assert.IsType<VerificationRecord>(await ObjectStoreVerification.ReadRecordAsync(store, T.Ct));
        Assert.True(record.Understood);
        Assert.True(record.Verifies(ObjectStoreVerification.MinimumSuite));
        Assert.Equal(ObjectStoreVerification.Suite, record.Suite);
        Assert.Equal(ObjectStoreContractCases.Rounds, record.Rounds.Value);
        Assert.Equal(Now, record.VerifiedAt.Value);
        Assert.Equal(outcome.Record, record);
    }

    [Fact]
    public async Task the_record_is_what_the_format_says()
    {
        MemoryObjectStore store = new();
        await ObjectStoreVerification.RunAsync(store, Options(rounds: 8), T.Ct);
        Assert.Equal(
            "{\n  \"format\": 1,\n  \"suite\": 1,\n  \"racers\": 16,\n  \"rounds\": 8,\n  \"at\": \"2026-11-16T12:00:00Z\"\n}",
            Encoding.UTF8.GetString(store.Get(Record)!));
    }

    [Fact]
    public async Task a_second_passing_run_replaces_the_record()
    {
        MemoryObjectStore store = new();
        await ObjectStoreVerification.RunAsync(store, Options(rounds: 1), T.Ct);
        VerificationOutcome second = await ObjectStoreVerification.RunAsync(store, Options(rounds: 2), T.Ct);

        Assert.True(second.Verified);
        Assert.Equal(2, (await ObjectStoreVerification.ReadRecordAsync(store, T.Ct))!.Rounds.Value);
        Assert.Equal([Record], store.Names());
    }

    /// <summary>
    /// The first of the gate's two broken stores: one that ignores conditions,
    /// as Garage does. The first case is the counterexample, and a record a
    /// past run left is removed.
    /// </summary>
    [Fact]
    public async Task a_store_that_ignores_conditions_fails_with_the_counterexample_and_loses_its_record()
    {
        MemoryObjectStore store = new();
        Assert.True((await ObjectStoreVerification.RunAsync(store, Options(), T.Ct)).Verified);
        store.IgnoresConditions = true;

        VerificationOutcome outcome = await ObjectStoreVerification.RunAsync(store, Options(), T.Ct);

        Assert.False(outcome.Verified);
        VerificationCounterexample counterexample = Assert.IsType<VerificationCounterexample>(outcome.Counterexample);
        Assert.Equal("create-if-absent writes once and reads back", counterexample.Case.Value);
        Assert.Equal("a second create of the same name returned a version", counterexample.Violation.Value);
        Assert.Empty(outcome.Passed);
        Assert.Null(outcome.Record);
        Assert.Empty(store.Names());
        Assert.Null(await ObjectStoreVerification.ReadRecordAsync(store, T.Ct));
    }

    /// <summary>
    /// The second: one that enforces each condition a request at a time and
    /// loses races. **The open's probe passes it** (ADR 0134 decision 9), and
    /// a dataset opens for write on it; the suite's race property is what
    /// refuses it, which is why the suite is the evidence (ADR 0183).
    /// </summary>
    [Fact]
    public async Task a_store_that_loses_races_passes_the_probe_and_fails_the_race_property()
    {
        await using (ObjectHarness harness = new(new MemoryObjectStore { LosesRaces = true }))
        {
            Backend storage = await harness.OpenAsync();
            Assert.NotNull(storage.Epoch);
        }

        MemoryObjectStore store = new() { LosesRaces = true };
        VerificationOutcome outcome = await ObjectStoreVerification.RunAsync(store, Options(), T.Ct);

        Assert.False(outcome.Verified);
        VerificationCounterexample counterexample = Assert.IsType<VerificationCounterexample>(outcome.Counterexample);
        Assert.Equal("of clients racing create-if-absent on one name exactly one wins", counterexample.Case.Value);
        Assert.StartsWith("round 0: ", counterexample.Violation.Value, StringComparison.Ordinal);
        Assert.EndsWith(" of 16 racing creates were told they wrote", counterexample.Violation.Value, StringComparison.Ordinal);

        // The five operations, one request at a time, are all it keeps.
        Assert.Equal(ObjectStoreVerification.Cases().Take(5).Select(c => c.Name), outcome.Passed.Select(c => c.Value));
        Assert.Empty(store.Names());
    }

    /// <summary>The replace race alone refuses a store whose creates hold: the writer record rests on it (ADR 0136).</summary>
    [Fact]
    public async Task a_store_that_loses_only_replace_races_fails_the_replace_race()
    {
        MemoryObjectStore store = new() { LosesRaces = true, CreatesHold = true };

        VerificationOutcome outcome = await ObjectStoreVerification.RunAsync(store, Options(), T.Ct);

        Assert.Equal("of clients racing replace-if-unchanged on one version exactly one wins", outcome.Counterexample?.Case.Value);
        Assert.Equal(6, outcome.Passed.Count);
    }

    [Fact]
    public async Task two_runs_at_once_never_see_each_others_objects()
    {
        MemoryObjectStore store = new();
        VerificationOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ObjectStoreVerification.RunAsync(store, Options(), T.Ct)));

        Assert.All(outcomes, outcome => Assert.True(outcome.Verified, outcome.Counterexample?.ToString()));
        Assert.Equal([Record], store.Names());
    }

    /// <summary>
    /// A store that cannot be reached is no verdict: the failure is thrown,
    /// no record is written, and one a past run left stays.
    /// </summary>
    [Fact]
    public async Task a_store_that_fails_is_an_exception_and_not_a_verdict()
    {
        MemoryObjectStore store = new();
        await ObjectStoreVerification.RunAsync(store, Options(), T.Ct);
        byte[] kept = store.Get(Record)!;
        store.Hook = (operation, _) => operation == ObjectOperation.Read ? throw new IOException("the store is away") : null;

        IOException thrown = await Assert.ThrowsAsync<IOException>(() => ObjectStoreVerification.RunAsync(store, Options(), T.Ct));

        Assert.Equal("the store is away", thrown.Message);
        Assert.Equal(kept, store.Get(Record));
    }

    [Fact]
    public async Task scratch_objects_that_cannot_be_removed_are_named_and_the_verdict_stands()
    {
        MemoryObjectStore store = new();
        store.Hook = (operation, name) => operation == ObjectOperation.Delete && name.Value.EndsWith("/empty", StringComparison.Ordinal)
            ? throw new IOException("no delete today")
            : null;

        VerificationOutcome outcome = await ObjectStoreVerification.RunAsync(store, Options(), T.Ct);

        Assert.True(outcome.Verified);
        ObjectName left = Assert.Single(outcome.LeftBehind);
        Assert.StartsWith("varve-verify/", left.Value, StringComparison.Ordinal);
        Assert.EndsWith("/0/empty", left.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// A record this build does not read is not a verification (ADRs 0183,
    /// 0217): another format, no format, or bytes that are not the record's.
    /// </summary>
    [Theory]
    [InlineData("{\"format\": 2, \"suite\": 9, \"racers\": 16, \"rounds\": 8, \"at\": \"2026-11-16T12:00:00Z\"}")]
    [InlineData("{\"suite\": 1, \"racers\": 16, \"rounds\": 8, \"at\": \"2026-11-16T12:00:00Z\"}")]
    [InlineData("{\"format\": 1, \"suite\": 0, \"racers\": 16, \"rounds\": 8, \"at\": \"2026-11-16T12:00:00Z\"}")]
    [InlineData("{\"format\": 1, \"suite\": 1, \"racers\": 16, \"rounds\": 8, \"at\": \"yesterday\"}")]
    [InlineData("{\"format\": 1}")]
    [InlineData("[1]")]
    [InlineData("verified")]
    [InlineData("")]
    public async Task a_record_this_build_does_not_read_is_not_a_verification(string bytes)
    {
        MemoryObjectStore store = new();
        store.Put(Record, Encoding.UTF8.GetBytes(bytes));

        VerificationRecord record = Assert.IsType<VerificationRecord>(await ObjectStoreVerification.ReadRecordAsync(store, T.Ct));

        Assert.False(record.Understood);
        Assert.False(record.Verifies(ObjectStoreVerification.MinimumSuite));
        Assert.False(record.Verifies(new VerificationSuite(1)));
    }

    [Fact]
    public async Task a_record_of_an_older_suite_does_not_verify_for_a_newer_minimum_and_no_record_is_none()
    {
        MemoryObjectStore store = new();
        Assert.Null(await ObjectStoreVerification.ReadRecordAsync(store, T.Ct));

        await ObjectStoreVerification.RunAsync(store, Options(), T.Ct);
        VerificationRecord record = (await ObjectStoreVerification.ReadRecordAsync(store, T.Ct))!;
        Assert.True(record.Verifies(new VerificationSuite(1)));
        Assert.False(record.Verifies(new VerificationSuite(2)));

        // A later release's record, of a later suite, read by this one.
        store.Put(Record, Encoding.UTF8.GetBytes("{\"format\": 1, \"suite\": 3, \"racers\": 16, \"rounds\": 64, \"at\": \"2027-01-01T00:00:00Z\", \"later\": true}"));
        VerificationRecord later = (await ObjectStoreVerification.ReadRecordAsync(store, T.Ct))!;
        Assert.True(later.Verifies(new VerificationSuite(2)));
        Assert.Equal(64, later.Rounds.Value);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
