// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>A clock the test moves by hand, backwards included.</summary>
internal sealed class ManualClock : TimeProvider
{
    public ManualClock(DateTimeOffset start) => Now = start;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;

    public static ManualClock Epoch() => new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
}

internal static class T
{
    /// <summary>The test's cancellation token, which every store call takes.</summary>
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + local));

    public static RdfTerm Blank(string label) => RdfTerm.BlankNode(Encoding.UTF8.GetBytes(label));

    public static RdfTerm Literal(string lexical) => RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical));

    public static RdfTerm Integer(string lexical) =>
        RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical), RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8));

    public static RdfTerm Boolean(string lexical) =>
        RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical), RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#boolean"u8));

    public static RdfTerm Lang(string lexical, string language) =>
        RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical), Encoding.UTF8.GetBytes(language));

    /// <remarks>
    /// Background merges of extent sets are off (ADR 0158): the suites that
    /// take and drop checkpoints state what the checkpoints are, and several
    /// open a second dataset over one memory storage, which only one process
    /// may write. The merge suites turn merges on and drive maintenance.
    /// </remarks>
    public static DatasetOptions Options(TimeProvider? clock = null, int maxRecordBytes = 1 << 20, long segmentBytes = 64L << 20, GroupCommitOptions? groupCommit = null) =>
        new() { Clock = clock ?? ManualClock.Epoch(), MaxRecordBytes = new ByteCount(maxRecordBytes), SegmentBytes = new ByteCount(segmentBytes), CommitCache = CommitCache, GroupCommit = groupCommit, ExtentMerges = ExtentMergePolicy.Off };

    /// <summary>The commit index's cache in tests: two entries, so that every suite pages the index out, merges it and reads it back (ADR 0089).</summary>
    public const int CommitCache = 2;

    /// <summary>The id every test dataset is created with, so that two runs write the same bytes.</summary>
    public static DatasetId Id { get; } = new(new Guid("6a0e7b3c-1d2f-4a5b-8c9d-0e1f2a3b4c5d"));

    /// <summary>The manifest of a dataset created with <see cref="Id"/>, for copies of a log made from its segments.</summary>
    public static ReadOnlyMemory<byte> Manifest => LogFormat.EncodeManifest(Id);

    public static ValueTask<Dataset> Open(IStorage storage, TimeProvider? clock = null, GroupCommitOptions? groupCommit = null) => OpenOrCreate(storage, Options(clock, groupCommit: groupCommit));

    /// <summary>Opens the dataset in the storage, creating it with <see cref="Id"/> when the storage is empty.</summary>
    public static async ValueTask<Dataset> OpenOrCreate(IStorage storage, DatasetOptions options) =>
        (await storage.Log.ReadManifestAsync(Ct)).IsEmpty && (await storage.Log.ListSegmentsAsync(Ct)).Count == 0
            ? await Dataset.CreateAsync(storage, Id, options, Ct)
            : await Dataset.OpenAsync(storage, options, Ct);

    /// <summary>A copy of a log as bytes: its manifest and its segments.</summary>
    public static async Task<(ReadOnlyMemory<byte> Manifest, List<byte[]> Segments)> CopyLogAsync(IStorage storage)
    {
        List<byte[]> segments = [];

        foreach (SegmentInfo segment in await storage.Log.ListSegmentsAsync(Ct))
        {
            segments.Add((await storage.Log.ReadRangeAsync(segment.Id, new ByteOffset(0), segment.Length, Ct)).ToArray());
        }

        return ((await storage.Log.ReadManifestAsync(Ct)).ToArray(), segments);
    }

    /// <summary>A whole derived blob, read through the synchronous blob read.</summary>
    public static async Task<ReadOnlyMemory<byte>> ReadBlobAsync(IStorage storage, BlobName name)
    {
        using IReadableBlob blob = await storage.Derived.OpenAsync(name, Ct);
        byte[] bytes = new byte[blob.Length.Value];
        return bytes.AsMemory(0, blob.Read(new ByteOffset(0), bytes));
    }

    /// <summary>
    /// What a checkpoint is in derived/ (ADR 0139): the catalog and every
    /// blob of the sets it names, each with its bytes. A set no catalog names —
    /// one an object store keeps for Extent maintenance — is not part of it.
    /// </summary>
    public static async Task<List<(BlobName Name, ReadOnlyMemory<byte> Bytes)>> CheckpointBlobsAsync(IStorage storage, DatasetId? dataset = null)
    {
        List<(BlobName, ReadOnlyMemory<byte>)> blobs = [];
        var catalog = await ExtentCatalog.TryReadAsync(storage.Derived, dataset ?? Id, Ct);

        if (catalog is not { } read)
        {
            return blobs;
        }

        // The catalog as written, but for the backend's slot in its cursor:
        // that is the backend's own place in its log — a chunk number in an
        // object store, nothing on a directory (ADR 0139) — and every other
        // byte is the same on every backend.
        MemoryStorage canonical = new();
        await ExtentCatalog.WriteAsync(canonical.Derived, dataset ?? Id, read.Header.To, read.Header.ToHash, read.Sequence, read.Sets, read.Cursor is { } cursor ? cursor with { Slot = 0 } : null, Ct);
        blobs.Add((ExtentCatalog.Name, await ReadBlobAsync(canonical, ExtentCatalog.Name)));
        HashSet<BlobName> listed = [.. await storage.Derived.ListAsync(Ct)];

        foreach (CatalogEntry set in read.Sets)
        {
            foreach (BlobName part in ExtentSet.BlobsOf(set.Name).Where(listed.Contains))
            {
                blobs.Add((part, await ReadBlobAsync(storage, part)));
            }
        }

        return blobs;
    }

    public static List<Quad> Drain(IQuadCursor cursor)
    {
        using (cursor)
        {
            List<Quad> quads = [];

            while (cursor.MoveNext())
            {
                quads.Add(cursor.Current);
            }

            return quads;
        }
    }

    public static List<Quad> All(IQuadSource source) =>
        Drain(source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any));

    /// <summary>Every quad of a source as terms, as N-Quads-ish strings, sorted: comparable across sources.</summary>
    public static SortedSet<string> Terms(IQuadSource source)
    {
        SortedSet<string> result = new(StringComparer.Ordinal);

        foreach (Quad quad in All(source))
        {
            result.Add(Render(source, quad));
        }

        return result;
    }

    public static string Render(IQuadSource source, Quad quad) =>
        Render(source, quad.Subject) + " " + Render(source, quad.Predicate) + " " + Render(source, quad.Object)
        + (quad.Graph.IsNone ? string.Empty : " " + Render(source, quad.Graph));

    public static string Render(IQuadSource source, TermHandle handle) =>
        source.TryExternalise(handle, out RdfTerm? term) ? Render(term) : "?" + handle.Value;

    public static string Render(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.Iri => "<" + Encoding.UTF8.GetString(term.Lexical) + ">",
        RdfTermKind.BlankNode => "_:" + Encoding.UTF8.GetString(term.Lexical),
        RdfTermKind.TripleTerm => "<<( " + Render(term.Subject!) + " " + Render(term.Predicate!) + " " + Render(term.Object!) + " )>>",
        _ => "\"" + Encoding.UTF8.GetString(term.Lexical) + "\""
            + (term.Language.Length > 0 ? "@" + Encoding.UTF8.GetString(term.Language).ToLowerInvariant() + "--" + term.Direction : "^^<" + Encoding.UTF8.GetString(term.DatatypeIri) + ">"),
    };
}

/// <summary>
/// A clock whose timers fire at once: for suites that make a backend retry
/// many times, where how long a retry waits is no part of what is tested
/// (ADR 0136: a wait decides how soon, never whether).
/// </summary>
internal sealed class InstantClock : TimeProvider
{
    public InstantClock(DateTimeOffset start) => Now = start;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        new Instant(callback, state, dueTime);

    private sealed class Instant : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public Instant(TimerCallback callback, object? state, TimeSpan dueTime)
        {
            _callback = callback;
            _state = state;
            Change(dueTime, Timeout.InfiniteTimeSpan);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                ThreadPool.QueueUserWorkItem(_ => _callback(_state));
            }

            return true;
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
