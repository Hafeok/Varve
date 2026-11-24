// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// The object-store contract suite (ADRs 0134, 0183): what every store must
/// do for a log to live in it, as cases anyone can run against their own
/// store. The five operations, and the two races the epoch fence and the
/// writer record rest on (ADR 0136).
/// </summary>
/// <remarks>
/// The probe an open runs (ADR 0134 decision 9) refuses a store that ignores
/// conditional writes. Only the races here detect one that enforces them a
/// request at a time and loses under concurrency.
/// </remarks>
[Contract(typeof(VerifiedObjectStores.TheContractSuiteIsShippedCode), Role = "the object-store contract suite, run against any store")]
public static class ObjectStoreVerification
{
    private const int RacerCount = 16;

    /// <summary>How many clients race in each round of a race: sixteen (ADR 0134 decision 7).</summary>
    public static VerificationRacers Racers => new(RacerCount);

    private const string ScratchRoot = "varve-verify/";

    /// <summary>The suite this build runs.</summary>
    public static VerificationSuite Suite => new(1);

    /// <summary>The oldest suite whose verification record this build accepts.</summary>
    public static VerificationSuite MinimumSuite => new(1);

    /// <summary>The verification record's name, at the store's root.</summary>
    public static ObjectName RecordName { get; } = new("varve-verification");

    /// <summary>
    /// The cases, in the order a run makes them. Each takes a store, a
    /// prefix of its own and the rounds of a race, and throws
    /// <see cref="ContractBrokenException"/> when the store breaks the contract.
    /// </summary>
    internal static (string Name, Func<IObjectStore, string, int, CancellationToken, Task> Run)[] Cases() =>
    [
        ("create-if-absent writes once and reads back", (store, prefix, _, ct) => CreateWritesOnce(store, prefix, ct)),
        ("replace-if-unchanged needs the current version", (store, prefix, _, ct) => ReplaceNeedsTheCurrentVersion(store, prefix, ct)),
        ("a ranged read returns the range, short at the end, none for no object", (store, prefix, _, ct) => RangedRead(store, prefix, ct)),
        ("a list returns the prefix's objects in ordinal order with their lengths", (store, prefix, _, ct) => ListByPrefix(store, prefix, ct)),
        ("a delete deletes once", (store, prefix, _, ct) => DeleteOnce(store, prefix, ct)),
        ("of clients racing create-if-absent on one name exactly one wins", CreateRace),
        ("of clients racing replace-if-unchanged on one version exactly one wins", ReplaceRace),
    ];

    /// <summary>
    /// Runs the suite against <paramref name="store"/> in a scratch prefix of
    /// its root, removes the prefix, and writes the verification record when
    /// every case passed, or removes it when one did not.
    /// </summary>
    /// <returns>The verdict: a pass, or the first case the store broke.</returns>
    /// <remarks>
    /// A store that cannot be reached is an exception, as from any use of it:
    /// that says nothing about the contract.
    /// </remarks>
    public static async Task<VerificationOutcome> RunAsync(IObjectStore store, VerificationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        // A random prefix per run: two runs at once never see each other's objects.
        byte[] random = new byte[16];
        options.Randomness.Fill(random);
        string scratch = ScratchRoot + Convert.ToHexStringLower(random) + "/";
        List<VerificationCaseName> passed = [];
        VerificationCounterexample? counterexample = null;
        IReadOnlyList<ObjectName> left;

        (string Name, Func<IObjectStore, string, int, CancellationToken, Task> Run)[] cases = Cases();

        try
        {
            for (int i = 0; i < cases.Length && counterexample is null; i++)
            {
                (string name, Func<IObjectStore, string, int, CancellationToken, Task> run) = cases[i];

                try
                {
                    await run(store, scratch + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/", options.Rounds.Value, cancellationToken).ConfigureAwait(false);
                    passed.Add(new VerificationCaseName(name));
                }
                catch (ContractBrokenException broken)
                {
                    counterexample = new VerificationCounterexample(new VerificationCaseName(name), new ContractViolationText(broken.Message));
                }
            }
        }
        finally
        {
            left = await RemoveAsync(store, scratch, cancellationToken).ConfigureAwait(false);
        }

        VerificationRecord? record = null;

        if (counterexample is null)
        {
            record = new VerificationRecord { Understood = true, Suite = Suite, Rounds = options.Rounds, VerifiedAt = new VerificationTime(options.Clock.GetUtcNow()) };
            await WriteRecordAsync(store, record, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // A store that no longer passes is not left marked as one that did.
            await store.DeleteAsync(RecordName, cancellationToken).ConfigureAwait(false);
        }

        return new VerificationOutcome
        {
            Suite = Suite,
            Rounds = options.Rounds,
            Passed = passed,
            Counterexample = counterexample,
            LeftBehind = left,
            Record = record,
        };
    }

    /// <summary>
    /// The verification record at the root of <paramref name="store"/>, or
    /// none. One whose format this build does not read is returned as not
    /// understood, and is not a verification (ADR 0217).
    /// </summary>
    public static async ValueTask<VerificationRecord?> ReadRecordAsync(IObjectStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ObjectRead? read = await ConditionalWrites.ReadWholeAsync(store, RecordName, cancellationToken).ConfigureAwait(false);
        return read is { } found ? VerificationRecordFormat.Read(found.Bytes.Span) : null;
    }

    // The record replaces whatever was there: a create when there is none, a
    // replace against the version read when there is. A race with another
    // run is tried again; both wrote a pass, so either record stands.
    private static async ValueTask WriteRecordAsync(IObjectStore store, VerificationRecord record, CancellationToken cancellationToken)
    {
        byte[] bytes = VerificationRecordFormat.Write(record);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            ObjectRead? current = await store.ReadAsync(RecordName, new ByteOffset(0), new ByteCount(1), cancellationToken).ConfigureAwait(false);
            ObjectVersion? written = current is { } found
                ? await store.ReplaceIfUnchangedAsync(RecordName, found.Version, bytes, cancellationToken).ConfigureAwait(false)
                : await store.CreateIfAbsentAsync(RecordName, bytes, cancellationToken).ConfigureAwait(false);

            if (written is not null)
            {
                return;
            }
        }

        throw new IOException("The verification record '" + RecordName + "' at " + store + " changed under four attempts to write it.");
    }

    // Every object under the run's prefix, listed and deleted. What a failed
    // delete leaves is named, never thrown: the verdict is the run's result.
    private static async ValueTask<IReadOnlyList<ObjectName>> RemoveAsync(IObjectStore store, string scratch, CancellationToken cancellationToken)
    {
        List<ObjectName> left = [];
        IReadOnlyList<ObjectEntry> entries;

        try
        {
            entries = await store.ListAsync(new ObjectName(scratch), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or HttpRequestException)
        {
            return [new ObjectName(scratch)];
        }

        foreach (ObjectEntry entry in entries)
        {
            try
            {
                await store.DeleteAsync(entry.Name, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or HttpRequestException)
            {
                left.Add(entry.Name);
            }
        }

        return left;
    }

    private static async Task CreateWritesOnce(IObjectStore store, string prefix, CancellationToken ct)
    {
        ObjectName name = new(prefix + "a");
        ObjectVersion? first = await store.CreateIfAbsentAsync(name, Bytes(1, 2, 3), ct).ConfigureAwait(false);
        Check(first is not null, "the first create returned no version");
        Check(await store.CreateIfAbsentAsync(name, Bytes(9), ct).ConfigureAwait(false) is null, "a second create of the same name returned a version");

        ObjectRead read = await store.ReadAsync(name, new ByteOffset(0), new ByteCount(100), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("the created object does not read");
        Check(read.Bytes.Span.SequenceEqual(Bytes(1, 2, 3)), "the object does not hold what the first create wrote");
        Check(read.Version == first, "a read names another version than the create returned");
        Check(read.Length.Value == 3, "a read reports the wrong length");

        ObjectVersion? empty = await store.CreateIfAbsentAsync(new ObjectName(prefix + "empty"), ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
        Check(empty is not null, "an empty object cannot be created");
        ObjectRead emptyRead = await store.ReadAsync(new ObjectName(prefix + "empty"), new ByteOffset(0), new ByteCount(10), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("an empty object does not read");
        Check(emptyRead.Bytes.IsEmpty && emptyRead.Length.Value == 0, "an empty object reads as bytes");
    }

    private static async Task ReplaceNeedsTheCurrentVersion(IObjectStore store, string prefix, CancellationToken ct)
    {
        ObjectName name = new(prefix + "r");
        ObjectVersion v1 = await store.CreateIfAbsentAsync(name, Bytes(1), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("create failed");
        ObjectVersion v2 = await store.ReplaceIfUnchangedAsync(name, v1, Bytes(2), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("a replace against the current version failed");
        Check(v2 != v1, "a replace kept the version it replaced");
        Check(await store.ReplaceIfUnchangedAsync(name, v1, Bytes(3), ct).ConfigureAwait(false) is null, "a replace against a superseded version succeeded");

        ObjectRead read = await store.ReadAsync(name, new ByteOffset(0), new ByteCount(10), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("the object does not read");
        Check(read.Bytes.Span.SequenceEqual(Bytes(2)) && read.Version == v2, "the object is not the winning replace's");
        Check(await store.ReplaceIfUnchangedAsync(new ObjectName(prefix + "none"), v2, Bytes(4), ct).ConfigureAwait(false) is null, "a replace of no object succeeded");
        Check(await store.ReadAsync(new ObjectName(prefix + "none"), new ByteOffset(0), new ByteCount(10), ct).ConfigureAwait(false) is null, "a replace of no object created one");
    }

    private static async Task RangedRead(IObjectStore store, string prefix, CancellationToken ct)
    {
        ObjectName name = new(prefix + "range");
        byte[] content = [.. Enumerable.Range(0, 1000).Select(i => (byte)i)];
        await store.CreateIfAbsentAsync(name, content, ct).ConfigureAwait(false);

        ObjectRead middle = await store.ReadAsync(name, new ByteOffset(100), new ByteCount(50), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("no read");
        Check(middle.Bytes.Span.SequenceEqual(content.AsSpan(100, 50)), "a ranged read returned other bytes");
        Check(middle.Length.Value == 1000, "a ranged read reports a length other than the object's");

        ObjectRead tail = await store.ReadAsync(name, new ByteOffset(990), new ByteCount(50), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("no read");
        Check(tail.Bytes.Span.SequenceEqual(content.AsSpan(990)), "a read past the end is not short");

        ObjectRead past = await store.ReadAsync(name, new ByteOffset(1000), new ByteCount(10), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("no read");
        Check(past.Bytes.IsEmpty, "a read from the end returned bytes");

        ObjectRead whole = await store.ReadAsync(name, new ByteOffset(0), new ByteCount(long.MaxValue), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("no read");
        Check(whole.Bytes.Length <= 1000 && whole.Bytes.Span.SequenceEqual(content.AsSpan(0, whole.Bytes.Length)), "a read of everything returned other bytes");
        Check(await store.ReadAsync(new ObjectName(prefix + "missing"), new ByteOffset(0), new ByteCount(1), ct).ConfigureAwait(false) is null, "a read of no object returned bytes");
    }

    private static async Task ListByPrefix(IObjectStore store, string prefix, CancellationToken ct)
    {
        foreach (string name in new[] { "l/b", "l/a", "l/c/d", "lx", "m" })
        {
            await store.CreateIfAbsentAsync(new ObjectName(prefix + name), Bytes(1, 2), ct).ConfigureAwait(false);
        }

        IReadOnlyList<ObjectEntry> listed = await store.ListAsync(new ObjectName(prefix + "l/"), ct).ConfigureAwait(false);
        Check(listed.Select(e => e.Name.Value).SequenceEqual([prefix + "l/a", prefix + "l/b", prefix + "l/c/d"]), "a list returned " + string.Join(", ", listed.Select(e => e.Name.Value)));
        Check(listed.All(e => e.Length.Value == 2), "a list reports the wrong lengths");
        Check((await store.ListAsync(new ObjectName(prefix + "nothing/"), ct).ConfigureAwait(false)).Count == 0, "a list of an empty prefix returned objects");
    }

    private static async Task DeleteOnce(IObjectStore store, string prefix, CancellationToken ct)
    {
        ObjectName name = new(prefix + "d");
        await store.CreateIfAbsentAsync(name, Bytes(1), ct).ConfigureAwait(false);
        Check(await store.DeleteAsync(name, ct).ConfigureAwait(false), "a delete of an object reported none");
        Check(!await store.DeleteAsync(name, ct).ConfigureAwait(false), "a second delete reported an object");
        Check(await store.ReadAsync(name, new ByteOffset(0), new ByteCount(1), ct).ConfigureAwait(false) is null, "a deleted object reads");
        Check(await store.CreateIfAbsentAsync(name, Bytes(2), ct).ConfigureAwait(false) is not null, "a deleted name cannot be created again");
    }

    // The property the epoch fence rests on (ADR 0136): of the racers
    // creating one name at once, exactly one is told it wrote, and the object
    // holds that one's bytes.
    private static async Task CreateRace(IObjectStore store, string prefix, int rounds, CancellationToken ct)
    {
        for (int round = 0; round < rounds; round++)
        {
            ObjectName name = new(prefix + "race/" + round.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ObjectVersion?[] results = await Task.WhenAll(Enumerable.Range(0, RacerCount).Select(i => Task.Run(async () => await store.CreateIfAbsentAsync(name, Bytes((byte)i), ct).ConfigureAwait(false), ct))).ConfigureAwait(false);
            int[] winners = [.. Enumerable.Range(0, RacerCount).Where(i => results[i] is not null)];
            Check(winners.Length == 1, Round(round) + winners.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of " + RacerCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " racing creates were told they wrote");
            ObjectRead read = await store.ReadAsync(name, new ByteOffset(0), new ByteCount(10), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("no read");
            Check(read.Bytes.Span.SequenceEqual(Bytes((byte)winners[0])), Round(round) + "the object holds another racer's bytes than the winner's");
        }
    }

    // The property the writer record rests on (ADR 0136): of the racers
    // replacing one version at once, exactly one is told it wrote.
    private static async Task ReplaceRace(IObjectStore store, string prefix, int rounds, CancellationToken ct)
    {
        for (int round = 0; round < rounds; round++)
        {
            ObjectName name = new(prefix + "replace-race/" + round.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ObjectVersion start = await store.CreateIfAbsentAsync(name, Bytes(255), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("create failed");
            ObjectVersion?[] results = await Task.WhenAll(Enumerable.Range(0, RacerCount).Select(i => Task.Run(async () => await store.ReplaceIfUnchangedAsync(name, start, Bytes((byte)i), ct).ConfigureAwait(false), ct))).ConfigureAwait(false);
            int[] winners = [.. Enumerable.Range(0, RacerCount).Where(i => results[i] is not null)];
            Check(winners.Length == 1, Round(round) + winners.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of " + RacerCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " racing replaces were told they wrote");
            ObjectRead read = await store.ReadAsync(name, new ByteOffset(0), new ByteCount(10), ct).ConfigureAwait(false) ?? throw new ContractBrokenException("no read");
            Check(read.Bytes.Span.SequenceEqual(Bytes((byte)winners[0])) && read.Version == results[winners[0]], Round(round) + "the object is not the winner's");
        }
    }

    private static string Round(int round) => "round " + round.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": ";

    private static byte[] Bytes(params byte[] bytes) => bytes;

    private static void Check(bool condition, string violation)
    {
        if (!condition)
        {
            throw new ContractBrokenException(violation);
        }
    }
}

/// <summary>A store broke a case of the contract suite: the message is what was seen.</summary>
internal sealed class ContractBrokenException : Exception
{
    public ContractBrokenException()
    {
    }

    public ContractBrokenException(string message)
        : base(message)
    {
    }

    public ContractBrokenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
