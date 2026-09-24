// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
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
        new() { Clock = clock ?? ManualClock.Epoch(), MaxRecordBytes = maxRecordBytes, SegmentBytes = segmentBytes };

    public static ValueTask<Dataset> Open(IStorage storage, TimeProvider? clock = null) => Dataset.OpenAsync(storage, Options(clock), Ct);

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
