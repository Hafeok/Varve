// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store.Tests;

/// <summary>
/// The storage contract of ADRs 0018, 0040 and 0071 as plain asynchronous
/// methods, with no test framework: xunit runs them on the desktop backends
/// (<c>StorageContractTests</c>), and the browser test app runs the
/// same file against the browser backends in headless Chromium (ADR 0084).
/// </summary>
/// <remarks>
/// A case takes a factory for fresh, empty storage and throws
/// <see cref="ContractViolation"/> when the backend breaks the contract.
/// </remarks>
internal static class StorageContractCases
{
    /// <summary>Every case, by name, for a runner that has no reflection to find them.</summary>
    public static (string Name, Func<Func<ValueTask<IStorage>>, Durability, CancellationToken, Task> Run)[] All() =>
    [
        ("appended bytes read back and a read past the end is short", (create, _, ct) => AppendedBytesReadBack(create, ct)),
        ("a sealed segment refuses appends and only the newest may be open", (create, _, ct) => SealedSegmentRefusesAppends(create, ct)),
        ("bytes already read never change", (create, _, ct) => BytesAlreadyReadNeverChange(create, ct)),
        ("derived blobs are published, replaced, listed in ordinal order and deleted", (create, _, ct) => DerivedBlobsLifecycle(create, ct)),
        ("a blob is invisible until published and an unpublished writer leaves nothing", (create, _, ct) => UnpublishedLeavesNothing(create, ct)),
        ("an open blob keeps its bytes when its name is replaced", (create, _, ct) => OpenBlobKeepsItsBytes(create, ct)),
        ("the manifest is empty until written and is written once before any segment", (create, _, ct) => ManifestWrittenOnce(create, ct)),
        ("the backend declares its durability", BackendDeclaresDurability),
        ("a dataset commits, checkpoints, reopens and reads as of over the backend", (create, _, ct) => DatasetOverTheBackend(create, ct)),
    ];

    /// <summary>The id every contract dataset is created with.</summary>
    public static DatasetId Id { get; } = new(new Guid("6a0e7b3c-1d2f-4a5b-8c9d-0e1f2a3b4c5d"));

    public static async Task AppendedBytesReadBack(Func<ValueTask<IStorage>> create, CancellationToken ct)
    {
        IStorage storage = await create();
        SegmentId segment = await storage.Log.CreateSegmentAsync(ct);
        await storage.Log.AppendAsync(segment, Bytes(1, 2, 3), ct);
        await storage.Log.AppendAsync(segment, Bytes(4, 5), ct);
        await storage.Log.FlushAsync(segment, ct);

        Equal(Bytes(1, 2, 3, 4, 5), (await storage.Log.ReadRangeAsync(segment, new ByteOffset(0), new ByteCount(100), ct)).ToArray());
        Equal(Bytes(3, 4), (await storage.Log.ReadRangeAsync(segment, new ByteOffset(2), new ByteCount(2), ct)).ToArray());
        Equal(0, (await storage.Log.ReadRangeAsync(segment, new ByteOffset(5), new ByteCount(10), ct)).Length);
    }

    public static async Task SealedSegmentRefusesAppends(Func<ValueTask<IStorage>> create, CancellationToken ct)
    {
        IStorage storage = await create();
        SegmentId first = await storage.Log.CreateSegmentAsync(ct);

        await Throws<InvalidOperationException>(async () => await storage.Log.CreateSegmentAsync(ct));

        await storage.Log.AppendAsync(first, Bytes(9), ct);
        await storage.Log.SealAsync(first, ct);
        await Throws<InvalidOperationException>(async () => await storage.Log.AppendAsync(first, Bytes(1), ct));

        SegmentId second = await storage.Log.CreateSegmentAsync(ct);
        True(second > first, "a new segment is numbered after the sealed one");

        IReadOnlyList<SegmentInfo> segments = await storage.Log.ListSegmentsAsync(ct);
        Equal([SegmentInfo.Sealed(first, new ByteCount(1)), SegmentInfo.Open(second, new ByteCount(0))], segments);
    }

    public static async Task BytesAlreadyReadNeverChange(Func<ValueTask<IStorage>> create, CancellationToken ct)
    {
        IStorage storage = await create();
        SegmentId segment = await storage.Log.CreateSegmentAsync(ct);
        await storage.Log.AppendAsync(segment, Bytes(1, 2), ct);
        ReadOnlyMemory<byte> held = await storage.Log.ReadRangeAsync(segment, new ByteOffset(0), new ByteCount(2), ct);

        // Enough appends to force any buffer the backend keeps to grow.
        for (int i = 0; i < 1000; i++)
        {
            await storage.Log.AppendAsync(segment, new byte[64], ct);
        }

        Equal(Bytes(1, 2), held.ToArray());
    }

    public static async Task DerivedBlobsLifecycle(Func<ValueTask<IStorage>> create, CancellationToken ct)
    {
        IStorage storage = await create();
        await PutAsync(storage, "b", ct, 1);
        await PutAsync(storage, "a", ct, 2, 3);
        await PutAsync(storage, "b", ct, 4, 5, 6);

        Equal([new BlobName("a"), new BlobName("b")], await storage.Derived.ListAsync(ct));
        Equal(Bytes(5, 6), await ReadAsync(storage, "b", ct, 1, 10));
        Equal(Bytes(4, 5, 6), await ReadAsync(storage, "b", ct));
        Equal(0, (await ReadAsync(storage, "b", ct, 3, 10)).Length);
        True(await storage.Derived.DeleteAsync(new BlobName("a"), ct), "deleting a blob that exists returns true");
        True(!await storage.Derived.DeleteAsync(new BlobName("a"), ct), "deleting a blob that does not exist returns false");
        Equal([new BlobName("b")], await storage.Derived.ListAsync(ct));
        await Throws<KeyNotFoundException>(async () => await storage.Derived.OpenAsync(new BlobName("a"), ct));
    }

    public static async Task UnpublishedLeavesNothing(Func<ValueTask<IStorage>> create, CancellationToken ct)
    {
        IStorage storage = await create();
        await PutAsync(storage, "kept", ct, 1, 2);

        await using (IBlobWriter writer = await storage.Derived.CreateAsync(new BlobName("kept"), ct))
        {
            await writer.WriteAsync(Bytes(9, 9, 9), ct);
            Equal(Bytes(1, 2), await ReadAsync(storage, "kept", ct));
        }

        await using (IBlobWriter writer = await storage.Derived.CreateAsync(new BlobName("never"), ct))
        {
            await writer.WriteAsync(Bytes(7), ct);
        }

        Equal(Bytes(1, 2), await ReadAsync(storage, "kept", ct));
        Equal([new BlobName("kept")], await storage.Derived.ListAsync(ct));
    }

    public static async Task OpenBlobKeepsItsBytes(Func<ValueTask<IStorage>> create, CancellationToken ct)
    {
        IStorage storage = await create();
        await PutAsync(storage, "run", ct, 1, 2, 3);
        using IReadableBlob held = await storage.Derived.OpenAsync(new BlobName("run"), ct);

        await PutAsync(storage, "run", ct, 4, 5, 6, 7);

        byte[] buffer = new byte[8];
        Equal(new ByteCount(3), held.Length);
        Equal(3, held.Read(new ByteOffset(0), buffer));
        Equal(Bytes(1, 2, 3), buffer.AsSpan(0, 3).ToArray());
        Equal(Bytes(4, 5, 6, 7), await ReadAsync(storage, "run", ct));
    }

    public static async Task ManifestWrittenOnce(Func<ValueTask<IStorage>> create, CancellationToken ct)
    {
        IStorage storage = await create();
        True((await storage.Log.ReadManifestAsync(ct)).IsEmpty, "a new log has no manifest");

        await storage.Log.WriteManifestAsync(Bytes(1, 2, 3), ct);
        Equal(Bytes(1, 2, 3), (await storage.Log.ReadManifestAsync(ct)).ToArray());
        await Throws<InvalidOperationException>(async () => await storage.Log.WriteManifestAsync(Bytes(4), ct));

        IStorage other = await create();
        await other.Log.CreateSegmentAsync(ct);
        await Throws<InvalidOperationException>(async () => await other.Log.WriteManifestAsync(Bytes(4), ct));
    }

    public static async Task BackendDeclaresDurability(Func<ValueTask<IStorage>> create, Durability expected, CancellationToken ct) =>
        Equal(expected, (await create()).Log.Durability);

    public static async Task DatasetOverTheBackend(Func<ValueTask<IStorage>> create, CancellationToken ct)
    {
        IStorage storage = await create();

        await using (Dataset dataset = await OpenOrCreateAsync(storage, Options(segmentBytes: 1024), ct))
        {
            for (int i = 0; i < 20; i++)
            {
                await dataset.CommitAsync(new CommitRequest().Assert(Iri("s" + i), Iri("p"), Literal(new string('x', 40))), ct);
            }

            await dataset.CheckpointAsync(new Position(10), ct);
        }

        True((await storage.Log.ListSegmentsAsync(ct)).Count > 1, "the small segment size should have forced several segments");

        await using Dataset reopened = await OpenOrCreateAsync(storage, Options(segmentBytes: 1024), ct);
        Equal(new Position(20), reopened.Head);
        Equal([new Position(10)], reopened.Checkpoints);

        using DatasetView at12 = await reopened.AsOfAsync(new Position(12), ct);
        Equal(12, Count(at12));
    }

    public static DatasetOptions Options(long segmentBytes = 64L << 20, TimeProvider? clock = null) =>
        new() { Clock = clock ?? new FixedClock(), SegmentBytes = new ByteCount(segmentBytes) };

    /// <summary>Opens the dataset in the storage, creating it with <see cref="Id"/> when the storage is empty.</summary>
    public static async ValueTask<Dataset> OpenOrCreateAsync(IStorage storage, DatasetOptions options, CancellationToken ct) =>
        (await storage.Log.ReadManifestAsync(ct)).IsEmpty && (await storage.Log.ListSegmentsAsync(ct)).Count == 0
            ? await Dataset.CreateAsync(storage, Id, options, ct)
            : await Dataset.OpenAsync(storage, options, ct);

    public static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + local));

    public static RdfTerm Literal(string lexical) => RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical));

    /// <summary>How many quads a source holds.</summary>
    public static int Count(IQuadSource source)
    {
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        int count = 0;

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    private static byte[] Bytes(params byte[] bytes) => bytes;

    private static async Task PutAsync(IStorage storage, string name, CancellationToken ct, params byte[] bytes)
    {
        await using IBlobWriter writer = await storage.Derived.CreateAsync(new BlobName(name), ct);
        await writer.WriteAsync(bytes, ct);
        await writer.PublishAsync(ct);
    }

    private static async Task<byte[]> ReadAsync(IStorage storage, string name, CancellationToken ct, long offset = 0, int length = 100)
    {
        using IReadableBlob blob = await storage.Derived.OpenAsync(new BlobName(name), ct);
        byte[] buffer = new byte[length];
        int read = blob.Read(new ByteOffset(offset), buffer);
        return buffer.AsSpan(0, read).ToArray();
    }

    private static void True(bool condition, string what)
    {
        if (!condition)
        {
            throw new ContractViolation(what);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new ContractViolation("expected " + expected + ", got " + actual);
        }
    }

    private static void Equal(byte[] expected, byte[] actual)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new ContractViolation("expected bytes [" + string.Join(", ", expected) + "], got [" + string.Join(", ", actual) + "]");
        }
    }

    private static void Equal<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
    {
        bool same = expected.Count == actual.Count;

        for (int i = 0; same && i < expected.Count; i++)
        {
            same = EqualityComparer<T>.Default.Equals(expected[i], actual[i]);
        }

        if (!same)
        {
            throw new ContractViolation("expected [" + string.Join(", ", expected) + "], got [" + string.Join(", ", actual) + "]");
        }
    }

    private static async Task Throws<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new ContractViolation("expected " + typeof(TException).Name + ", and nothing was thrown");
    }

    /// <summary>A clock that never moves: the contract cases read no time.</summary>
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    }
}

/// <summary>A backend broke the storage contract.</summary>
internal sealed class ContractViolation : Exception
{
    public ContractViolation()
    {
    }

    public ContractViolation(string message)
        : base(message)
    {
    }

    public ContractViolation(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
