// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The object-store contract of ADR 0134 as plain asynchronous methods: what
/// every adapter must do, run against the in-memory store here and against
/// Azurite, an S3-compatible server and, on the optional leg, a real account.
/// </summary>
/// <remarks>
/// Each case takes a store and a prefix of its own, so cases against a real
/// server never see each other's objects, and throws
/// <see cref="ContractViolation"/> when the store breaks the contract.
/// </remarks>
internal static class ObjectStoreContractCases
{
    /// <summary>How many clients race in the race property.</summary>
    public const int Racers = 16;

    public static (string Name, Func<IObjectStore, string, CancellationToken, Task> Run)[] All() =>
    [
        ("create-if-absent writes once and reads back", CreateWritesOnce),
        ("replace-if-unchanged needs the current version", ReplaceNeedsTheCurrentVersion),
        ("a ranged read returns the range, short at the end, none for no object", RangedRead),
        ("a list returns the prefix's objects in ordinal order with their lengths", ListByPrefix),
        ("a delete deletes once", DeleteOnce),
        ("of clients racing create-if-absent on one name exactly one wins", CreateRace),
        ("of clients racing replace-if-unchanged on one version exactly one wins", ReplaceRace),
    ];

    public static async Task CreateWritesOnce(IObjectStore store, string prefix, CancellationToken ct)
    {
        ObjectName name = new(prefix + "a");
        ObjectVersion? first = await store.CreateIfAbsentAsync(name, Bytes(1, 2, 3), ct);
        Check(first is not null, "the first create returned no version");
        Check(await store.CreateIfAbsentAsync(name, Bytes(9), ct) is null, "a second create of the same name returned a version");

        ObjectRead read = await store.ReadAsync(name, new ByteOffset(0), new ByteCount(100), ct) ?? throw new ContractViolation("the created object does not read");
        Check(read.Bytes.Span.SequenceEqual(Bytes(1, 2, 3)), "the object does not hold what the first create wrote");
        Check(read.Version == first, "a read names another version than the create returned");
        Check(read.Length.Value == 3, "a read reports the wrong length");

        ObjectVersion? empty = await store.CreateIfAbsentAsync(new ObjectName(prefix + "empty"), ReadOnlyMemory<byte>.Empty, ct);
        Check(empty is not null, "an empty object cannot be created");
        ObjectRead emptyRead = await store.ReadAsync(new ObjectName(prefix + "empty"), new ByteOffset(0), new ByteCount(10), ct) ?? throw new ContractViolation("an empty object does not read");
        Check(emptyRead.Bytes.IsEmpty && emptyRead.Length.Value == 0, "an empty object reads as bytes");
    }

    public static async Task ReplaceNeedsTheCurrentVersion(IObjectStore store, string prefix, CancellationToken ct)
    {
        ObjectName name = new(prefix + "r");
        ObjectVersion v1 = await store.CreateIfAbsentAsync(name, Bytes(1), ct) ?? throw new ContractViolation("create failed");
        ObjectVersion v2 = await store.ReplaceIfUnchangedAsync(name, v1, Bytes(2), ct) ?? throw new ContractViolation("a replace against the current version failed");
        Check(v2 != v1, "a replace kept the version it replaced");
        Check(await store.ReplaceIfUnchangedAsync(name, v1, Bytes(3), ct) is null, "a replace against a superseded version succeeded");

        ObjectRead read = await store.ReadAsync(name, new ByteOffset(0), new ByteCount(10), ct) ?? throw new ContractViolation("the object does not read");
        Check(read.Bytes.Span.SequenceEqual(Bytes(2)) && read.Version == v2, "the object is not the winning replace's");
        Check(await store.ReplaceIfUnchangedAsync(new ObjectName(prefix + "none"), v2, Bytes(4), ct) is null, "a replace of no object succeeded");
        Check(await store.ReadAsync(new ObjectName(prefix + "none"), new ByteOffset(0), new ByteCount(10), ct) is null, "a replace of no object created one");
    }

    public static async Task RangedRead(IObjectStore store, string prefix, CancellationToken ct)
    {
        ObjectName name = new(prefix + "range");
        byte[] content = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        await store.CreateIfAbsentAsync(name, content, ct);

        ObjectRead middle = await store.ReadAsync(name, new ByteOffset(100), new ByteCount(50), ct) ?? throw new ContractViolation("no read");
        Check(middle.Bytes.Span.SequenceEqual(content.AsSpan(100, 50)), "a ranged read returned other bytes");
        Check(middle.Length.Value == 1000, "a ranged read reports a length other than the object's");

        ObjectRead tail = await store.ReadAsync(name, new ByteOffset(990), new ByteCount(50), ct) ?? throw new ContractViolation("no read");
        Check(tail.Bytes.Span.SequenceEqual(content.AsSpan(990)), "a read past the end is not short");

        ObjectRead past = await store.ReadAsync(name, new ByteOffset(1000), new ByteCount(10), ct) ?? throw new ContractViolation("no read");
        Check(past.Bytes.IsEmpty, "a read from the end returned bytes");

        ObjectRead whole = await store.ReadAsync(name, new ByteOffset(0), new ByteCount(long.MaxValue), ct) ?? throw new ContractViolation("no read");
        Check(whole.Bytes.Length <= 1000 && whole.Bytes.Span.SequenceEqual(content.AsSpan(0, whole.Bytes.Length)), "a read of everything returned other bytes");
        Check(await store.ReadAsync(new ObjectName(prefix + "missing"), new ByteOffset(0), new ByteCount(1), ct) is null, "a read of no object returned bytes");
    }

    public static async Task ListByPrefix(IObjectStore store, string prefix, CancellationToken ct)
    {
        foreach (string name in new[] { "l/b", "l/a", "l/c/d", "lx", "m" })
        {
            await store.CreateIfAbsentAsync(new ObjectName(prefix + name), Bytes(1, 2), ct);
        }

        IReadOnlyList<ObjectEntry> listed = await store.ListAsync(new ObjectName(prefix + "l/"), ct);
        Check(listed.Select(e => e.Name.Value).SequenceEqual([prefix + "l/a", prefix + "l/b", prefix + "l/c/d"]), "a list returned " + string.Join(", ", listed.Select(e => e.Name.Value)));
        Check(listed.All(e => e.Length.Value == 2), "a list reports the wrong lengths");
        Check((await store.ListAsync(new ObjectName(prefix + "nothing/"), ct)).Count == 0, "a list of an empty prefix returned objects");
    }

    public static async Task DeleteOnce(IObjectStore store, string prefix, CancellationToken ct)
    {
        ObjectName name = new(prefix + "d");
        await store.CreateIfAbsentAsync(name, Bytes(1), ct);
        Check(await store.DeleteAsync(name, ct), "a delete of an object reported none");
        Check(!await store.DeleteAsync(name, ct), "a second delete reported an object");
        Check(await store.ReadAsync(name, new ByteOffset(0), new ByteCount(1), ct) is null, "a deleted object reads");
        Check(await store.CreateIfAbsentAsync(name, Bytes(2), ct) is not null, "a deleted name cannot be created again");
    }

    /// <summary>
    /// The property the epoch fence rests on (ADR 0136): of
    /// <see cref="Racers"/> clients creating one name at once, exactly one is
    /// told it wrote, and the object holds that one's bytes.
    /// </summary>
    public static async Task CreateRace(IObjectStore store, string prefix, CancellationToken ct)
    {
        for (int round = 0; round < 8; round++)
        {
            ObjectName name = new(prefix + "race/" + round);
            ObjectVersion?[] results = await Task.WhenAll(Enumerable.Range(0, Racers).Select(i => Task.Run(async () => await store.CreateIfAbsentAsync(name, Bytes((byte)i), ct), ct)));
            int[] winners = [.. Enumerable.Range(0, Racers).Where(i => results[i] is not null)];
            Check(winners.Length == 1, "round " + round + ": " + winners.Length + " of " + Racers + " racing creates were told they wrote");
            ObjectRead read = await store.ReadAsync(name, new ByteOffset(0), new ByteCount(10), ct) ?? throw new ContractViolation("no read");
            Check(read.Bytes.Span.SequenceEqual(Bytes((byte)winners[0])), "round " + round + ": the object holds another racer's bytes than the winner's");
        }
    }

    /// <summary>
    /// The property the writer record rests on (ADR 0136): of
    /// <see cref="Racers"/> clients replacing one version at once, exactly one
    /// is told it wrote.
    /// </summary>
    public static async Task ReplaceRace(IObjectStore store, string prefix, CancellationToken ct)
    {
        for (int round = 0; round < 8; round++)
        {
            ObjectName name = new(prefix + "replace-race/" + round);
            ObjectVersion start = await store.CreateIfAbsentAsync(name, Bytes(255), ct) ?? throw new ContractViolation("create failed");
            ObjectVersion?[] results = await Task.WhenAll(Enumerable.Range(0, Racers).Select(i => Task.Run(async () => await store.ReplaceIfUnchangedAsync(name, start, Bytes((byte)i), ct), ct)));
            int[] winners = [.. Enumerable.Range(0, Racers).Where(i => results[i] is not null)];
            Check(winners.Length == 1, "round " + round + ": " + winners.Length + " of " + Racers + " racing replaces were told they wrote");
            ObjectRead read = await store.ReadAsync(name, new ByteOffset(0), new ByteCount(10), ct) ?? throw new ContractViolation("no read");
            Check(read.Bytes.Span.SequenceEqual(Bytes((byte)winners[0])) && read.Version == results[winners[0]], "round " + round + ": the object is not the winner's");
        }
    }

    private static byte[] Bytes(params byte[] bytes) => bytes;

    private static void Check(bool condition, string violation)
    {
        if (!condition)
        {
            throw new ContractViolation(violation);
        }
    }
}
