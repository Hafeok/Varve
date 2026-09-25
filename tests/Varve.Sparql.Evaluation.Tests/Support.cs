// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Evaluation.Tests;

internal static class Support
{
    internal const string Ex = "http://example.org/";

    internal static RdfTerm Named(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes(Ex + local));

    internal static Query Parse(string text)
    {
        SparqlParseOptions options = new(Encoding.UTF8.GetBytes(Ex), SparqlVersion.Sparql12);
        return SparqlParser.TryParseQuery(Encoding.UTF8.GetBytes("PREFIX : <" + Ex + ">\n" + text), options, out Query? query, out SparqlParseError error)
            ? query
            : throw new InvalidOperationException("Does not parse: " + error + "\n" + text);
    }

    /// <summary>A SELECT's solutions as sorted lines of <c>?v=term</c>: a multiset, comparable with <c>Equal</c>.</summary>
    internal static List<string> Rows(QueryResults results)
    {
        SolutionResults solutions = (SolutionResults)results;
        List<string> rows = [];
        while (solutions.MoveNext())
        {
            List<string> cells = [];
            for (int i = 0; i < solutions.Variables.Count; i++)
            {
                if (solutions.TryGetTerm(i, out RdfTerm? term))
                {
                    cells.Add("?" + solutions.Variables[i].Name + "=" + Text(term!));
                }
            }

            rows.Add(string.Join(" ", cells));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    internal static List<string> Run(string query, IQuadSource source, EvaluationOptions? options = null)
    {
        using QueryResults results = new SparqlEvaluator(options ?? Options()).Evaluate(Parse(query), source);
        return Rows(results);
    }

    internal static EvaluationOptions Options(bool optimise = true) => new()
    {
        Clock = FixedClock.Instance,
        Randomness = new SeededRandom(),
        Optimise = optimise,
    };

    internal static string Text(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.Iri => "<" + Encoding.UTF8.GetString(term.Lexical) + ">",
        RdfTermKind.BlankNode => "_:" + Encoding.UTF8.GetString(term.Lexical),
        RdfTermKind.TripleTerm => "<<( " + Text(term.Subject!) + " " + Text(term.Predicate!) + " " + Text(term.Object!) + " )>>",
        _ => "\"" + Encoding.UTF8.GetString(term.Lexical) + "\""
            + (term.Language.IsEmpty ? "" : "@" + Encoding.UTF8.GetString(term.Language))
            + (term.Datatype is null ? "" : "^^<" + Encoding.UTF8.GetString(term.DatatypeIri) + ">"),
    };
}

internal sealed class FixedClock : TimeProvider
{
    internal static FixedClock Instance { get; } = new();

    public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class SeededRandom : IRandomSource
{
    private readonly Random _random = new(20260925);

    public void NextBytes(Span<byte> destination) => _random.NextBytes(destination);
}

/// <summary>A source whose every scan cancels the token after a number of quads: cancellation inside a running query.</summary>
internal sealed class CancellingSource(IQuadSource inner, CancellationTokenSource cancellation, int after) : IQuadSource
{
    private int _seen;

    private void Seen()
    {
        if (++_seen == after)
        {
            cancellation.Cancel();
        }
    }

    public IEqualityComparer<TermHandle> TermComparer => inner.TermComparer;

    public bool TryInternalise(RdfTerm term, out TermHandle handle) => inner.TryInternalise(term, out handle);

    public bool TryExternalise(TermHandle handle, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out RdfTerm term) => inner.TryExternalise(handle, out term);

    public bool Contains(in Quad quad) => inner.Contains(quad);

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        new Cursor(this, inner.Match(subject, predicate, @object, graph));

    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        inner.Estimate(subject, predicate, @object, graph);

    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => inner.TryGetInlineValue(handle, out value);

    private sealed class Cursor(CancellingSource owner, IQuadCursor inner) : IQuadCursor
    {
        public Quad Current => inner.Current;

        public bool MoveNext()
        {
            owner.Seen();
            return inner.MoveNext();
        }

        public void Dispose() => inner.Dispose();
    }
}
