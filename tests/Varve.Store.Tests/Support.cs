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

    public static DatasetOptions Options(TimeProvider? clock = null, int maxRecordBytes = 1 << 20, long segmentBytes = 64L << 20) =>
        new() { Clock = clock ?? ManualClock.Epoch(), MaxRecordBytes = new ByteCount(maxRecordBytes), SegmentBytes = new ByteCount(segmentBytes) };

    /// <summary>The id every test dataset is created with, so that two runs write the same bytes.</summary>
    public static DatasetId Id { get; } = new(new Guid("6a0e7b3c-1d2f-4a5b-8c9d-0e1f2a3b4c5d"));

    /// <summary>The manifest of a dataset created with <see cref="Id"/>, for copies of a log made from its segments.</summary>
    public static ReadOnlyMemory<byte> Manifest => LogFormat.EncodeManifest(Id);

    public static ValueTask<Dataset> Open(IStorage storage, TimeProvider? clock = null) => OpenOrCreate(storage, Options(clock));

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
