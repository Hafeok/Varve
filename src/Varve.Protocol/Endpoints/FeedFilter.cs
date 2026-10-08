// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Primitives;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// A feed's <c>graph</c> and <c>pattern</c> (<c>change-feed.md</c> §4). Terms
/// the dataset already knows become the subscription's own filter (ADR 0042:
/// the filter runs in the reader). A term it does not yet know matches
/// nothing until a commit allocates it, and from then on matches by handle,
/// learned from that commit's allocations (ADR 0097).
/// </summary>
internal sealed class FeedFilter
{
    private readonly RdfTerm?[] _terms = new RdfTerm?[4];
    private readonly TermHandle?[] _handles = new TermHandle?[4];
    private GraphMatch _graph = GraphMatch.Any;

    private FeedFilter()
    {
    }

    /// <summary>Whether the feed is unfiltered.</summary>
    internal bool IsAll => _graph == GraphMatch.Any && _terms[0] is null && _terms[1] is null && _terms[2] is null;

    /// <summary>Whether every term the filter names has a handle.</summary>
    internal bool IsResolved
    {
        get
        {
            for (int i = 0; i < 4; i++)
            {
                if (_terms[i] is not null && _handles[i] is null)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal static bool TryParse(StringValues graph, StringValues pattern, out FeedFilter filter, out string? error)
    {
        filter = new FeedFilter();
        error = null;

        if (graph.Count > 1 || pattern.Count > 1)
        {
            error = "Give graph and pattern at most once each.";
            return false;
        }

        if (graph.Count == 1)
        {
            if (graph[0] == "default")
            {
                filter._graph = GraphMatch.DefaultGraph;
            }
            else if (SparqlRequest.TryIri(graph[0], out RdfTerm iri))
            {
                filter._graph = GraphMatch.Named;
                filter._terms[3] = iri;
            }
            else
            {
                error = "graph is an absolute IRI, or default.";
                return false;
            }
        }

        if (pattern.Count == 1)
        {
            List<string> tokens = Tokens(pattern[0] ?? string.Empty);

            if (tokens.Count != 3)
            {
                error = "pattern is three terms in N-Triples syntax, each a term or ?name.";
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                if (tokens[i].StartsWith('?'))
                {
                    continue;
                }

                if (!ChangeFeedReader.TryParseTerm(Encoding.UTF8.GetBytes(tokens[i]), out RdfTerm? term) || term.Kind == RdfTermKind.BlankNode)
                {
                    error = "A pattern term is an IRI, a literal or a triple term in N-Triples syntax; a blank node label addresses nothing (ADR 0098).";
                    return false;
                }

                filter._terms[i] = term;
            }
        }

        return true;
    }

    /// <summary>Takes the handles the view knows.</summary>
    internal void Resolve(DatasetView view)
    {
        for (int i = 0; i < 4; i++)
        {
            if (_terms[i] is { } term && view.TryInternalise(term, out TermHandle handle))
            {
                _handles[i] = handle;
            }
        }
    }

    /// <summary>The store's filter, when every term is resolved.</summary>
    internal SubscriptionFilter ToSubscription() => IsAll
        ? SubscriptionFilter.All
        : SubscriptionFilter.ForPattern(Handle(0), Handle(1), Handle(2), _graph switch
        {
            GraphMatch.DefaultGraph => GraphPattern.DefaultGraph,
            GraphMatch.Named => GraphPattern.Named(Handle(3)),
            _ => GraphPattern.Any,
        });

    /// <summary>Learns handles from a commit's allocations, then keeps the changes that match.</summary>
    internal QuadDelta Apply(Commit commit)
    {
        if (IsAll)
        {
            return commit.Delta;
        }

        foreach (TermAllocation allocation in commit.Allocations.Span)
        {
            for (int i = 0; i < 4; i++)
            {
                if (_handles[i] is null && _terms[i] is { } term && term.Equals(allocation.Term))
                {
                    _handles[i] = allocation.Handle;
                }
            }
        }

        List<Quad> asserted = [];
        List<Quad> retracted = [];
        Keep(commit.Delta.Asserted, asserted);
        Keep(commit.Delta.Retracted, retracted);
        return QuadDelta.Create(asserted.ToArray(), retracted.ToArray());
    }

    private void Keep(ReadOnlySpan<Quad> quads, List<Quad> into)
    {
        foreach (Quad quad in quads)
        {
            if (Matches(0, quad.Subject) && Matches(1, quad.Predicate) && Matches(2, quad.Object) && MatchesGraph(quad.Graph))
            {
                into.Add(quad);
            }
        }
    }

    private bool Matches(int index, TermHandle handle) =>
        _terms[index] is null || (_handles[index] is TermHandle known && known == handle);

    private bool MatchesGraph(TermHandle graph) => _graph switch
    {
        GraphMatch.DefaultGraph => graph.IsNone,
        GraphMatch.Named => _handles[3] is TermHandle known && known == graph,
        _ => true,
    };

    private TermHandle Handle(int index) => _handles[index] ?? TermHandle.None;

    // Splits on spaces outside <…> and "…" (with \" escapes), so a literal may hold a space.
    private static List<string> Tokens(string text)
    {
        List<string> tokens = [];
        StringBuilder current = new();
        bool inIri = false;
        bool inString = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
                current.Append(c);

                if (c == '\\' && i + 1 < text.Length)
                {
                    current.Append(text[++i]);
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inIri)
            {
                current.Append(c);
                inIri = c != '>';
                continue;
            }

            if (c == ' ')
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            inIri = c == '<' && (current.Length == 0 || current[^1] == '^');
            inString = c == '"';
            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
