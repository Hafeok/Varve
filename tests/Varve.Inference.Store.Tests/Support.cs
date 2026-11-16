// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Inference.Model;
using Varve.Inference.Store.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Inference.Store.Tests;

/// <summary>A store, a binding, and an oracle that computes a binding's rows from a position's quads with nothing of the projection's.</summary>
internal static class Support
{
    internal const string RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    internal const string RdfsNs = "http://www.w3.org/2000/01/rdf-schema#";
    internal const string OwlNs = "http://www.w3.org/2002/07/owl#";

    internal static readonly RdfTerm TBox = Ex("tbox");

    internal static RdfTerm Ex(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://ex/" + local));

    internal static RdfTerm IriOf(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    internal static InferenceBinding Binding(InferenceProfile profile, bool defaultGraph = true) =>
        new(Ex("binding"), [TBox], GraphScope.Of([], ["http://ex/g"], defaultGraph ? DefaultGraphAccess.Included : DefaultGraphAccess.Excluded), profile);

    internal static async Task<Dataset> CreateAsync(CancellationToken ct) =>
        await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, ct);

    /// <summary>Commits toggles, each a triple in a graph, <see langword="null"/> the default graph: whether it committed.</summary>
    internal static async Task<bool> CommitAsync(Dataset dataset, IEnumerable<(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G, bool Assert)> toggles, CancellationToken ct)
    {
        CommitRequestBuilder request = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g, bool assert) in toggles)
        {
            if (g is null)
            {
                _ = assert ? request.Assert(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o))
                    : request.Retract(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o));
            }
            else
            {
                _ = assert ? request.Assert(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o), RequestTerm.FromTerm(g))
                    : request.Retract(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o), RequestTerm.FromTerm(g));
            }
        }

        CommitResult result = await dataset.CommitAsync(request, ct);
        return result.Outcome == CommitOutcome.Committed;
    }

    /// <summary>A projection's rows, as text, each its graph's and its fact's terms named through the view and the rows' own.</summary>
    internal static HashSet<string> Text(InferredRows rows, IQuadSource view)
    {
        HashSet<string> text = [];
        foreach (InferredRow row in rows.Rows)
        {
            text.Add(Name(row.Graph, rows, view) + " " + Name(row.Fact.Subject, rows, view) + " " + Name(row.Fact.Predicate, rows, view) + " " + Name(row.Fact.Object, rows, view));
        }

        return text;
    }

    private static string Name(TermHandle handle, InferredRows rows, IQuadSource view)
    {
        if (handle.IsNone)
        {
            return "DEFAULT";
        }

        if (rows.TryExternalise(handle, out RdfTerm? term) || view.TryExternalise(handle, out term))
        {
            return Name(term);
        }

        throw new InvalidOperationException("a row names a handle neither the view nor the projection knows: " + handle.Value);
    }

    internal static string Name(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.Iri => "<" + Encoding.UTF8.GetString(term.Lexical) + ">",
        RdfTermKind.BlankNode => "_:" + Encoding.UTF8.GetString(term.Lexical),
        _ => "\"" + Encoding.UTF8.GetString(term.Lexical) + "\"^^<" + Encoding.UTF8.GetString(term.DatatypeIri) + ">"
            + (term.Language.IsEmpty ? string.Empty : "@" + Encoding.UTF8.GetString(term.Language)),
    };

    /// <summary>
    /// The oracle: a binding's rows at a view's position, by ADR 0190's
    /// placement, over terms rather than the store's handles. The TBox
    /// closure's implicit, well-formed facts in the TBox graph; each data
    /// graph's closure's implicit, well-formed facts the TBox closure does not
    /// hold, in that graph.
    /// </summary>
    internal static HashSet<string> Expected(IQuadSource view, InferenceBinding binding)
    {
        TermTable terms = new();
        List<Fact> tbox = [];
        Dictionary<string, List<Fact>> graphs = [];
        using (IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
        {
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                RdfTerm? graph = null;
                if (!quad.Graph.IsNone && !view.TryExternalise(quad.Graph, out graph))
                {
                    throw new InvalidOperationException("a graph the view cannot name");
                }

                Fact fact = terms.Fact(Of(view, quad.Subject), Of(view, quad.Predicate), Of(view, quad.Object));
                if (graph is not null && graph.Equals(TBox))
                {
                    tbox.Add(fact);
                }
                else if (graph is null ? binding.DataGraphs.IncludesDefault : binding.DataGraphs.Allows(graph))
                {
                    string name = graph is null ? "DEFAULT" : Name(graph);
                    if (!graphs.TryGetValue(name, out List<Fact>? facts))
                    {
                        graphs[name] = facts = [];
                    }

                    facts.Add(fact);
                }
            }
        }

        string tboxGraph = Name(InferenceProjection.InferredTBox(binding.Binding.Lexical));
        HashSet<string> rows = [];
        Closure t = new(binding.Profile, terms);
        t.Apply([.. tbox], []);
        foreach (Fact fact in t.Implicit)
        {
            if (terms.Servable(fact))
            {
                rows.Add(tboxGraph + " " + terms.Text(fact));
            }
        }

        foreach ((string graph, List<Fact> facts) in graphs)
        {
            Closure g = new(binding.Profile, terms);
            g.Apply([.. tbox, .. facts], []);
            foreach (Fact fact in g.Implicit)
            {
                if (!t.Contains(fact) && terms.Servable(fact))
                {
                    rows.Add(graph + " " + terms.Text(fact));
                }
            }
        }

        return rows;
    }

    private static RdfTerm Of(IQuadSource view, TermHandle handle) =>
        view.TryExternalise(handle, out RdfTerm? term) ? term : throw new InvalidOperationException("a term the view cannot name");

    /// <summary>Handles over terms, for the oracle's closures.</summary>
    private sealed class TermTable : IInferenceTerms
    {
        private readonly Dictionary<RdfTerm, TermHandle> _handles = new(RdfTerm.Comparer);
        private readonly List<RdfTerm> _terms = [];

        public TermHandle Handle(RdfTerm term)
        {
            if (!_handles.TryGetValue(term, out TermHandle handle))
            {
                _terms.Add(term);
                handle = new TermHandle((ulong)_terms.Count);
                _handles[term] = handle;
            }

            return handle;
        }

        public bool IsLiteral(TermHandle handle) => Term(handle).Kind == RdfTermKind.Literal;

        public bool TryDatatype(TermHandle literal, out TermHandle datatype)
        {
            RdfTerm term = Term(literal);
            datatype = term.Kind == RdfTermKind.Literal ? Handle(RdfTerm.Iri(term.DatatypeIri)) : TermHandle.None;
            return term.Kind == RdfTermKind.Literal;
        }

        public bool IsContainerMembership(TermHandle handle)
        {
            RdfTerm term = Term(handle);
            string iri = Encoding.UTF8.GetString(term.Lexical);
            return term.Kind == RdfTermKind.Iri && iri.StartsWith(RdfNs + "_", StringComparison.Ordinal) && iri.Length > RdfNs.Length + 1
                && iri[RdfNs.Length + 1] != '0' && iri.AsSpan(RdfNs.Length + 1).IndexOfAnyExceptInRange('0', '9') < 0;
        }

        internal bool Servable(Fact fact) => Term(fact.Subject).Kind != RdfTermKind.Literal && Term(fact.Predicate).Kind == RdfTermKind.Iri;

        internal Fact Fact(RdfTerm s, RdfTerm p, RdfTerm o) => new(Handle(s), Handle(p), Handle(o));

        internal string Text(Fact fact) => Name(Term(fact.Subject)) + " " + Name(Term(fact.Predicate)) + " " + Name(Term(fact.Object));

        private RdfTerm Term(TermHandle handle) => _terms[(int)handle.Value - 1];
    }
}
