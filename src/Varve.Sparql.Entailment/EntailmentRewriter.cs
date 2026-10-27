// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Entailment.Closure;
using Varve.Sparql.Entailment.Model;

namespace Varve.Sparql.Entailment;

/// <summary>
/// A query rewritten under a regime, with the source to evaluate it over:
/// the request's source with the TBox graphs merged into every graph
/// (ADR 0193). The source is the caller's to keep alive, as the one it was
/// built from is.
/// </summary>
[Contract(typeof(RdfsRdfAndDByRewriting.TBoxMergedIntoEveryGraph), Role = "a rewritten query and the merge it is evaluated over")]
public sealed class EntailedQuery
{
    internal EntailedQuery(Query query, IQuadSource source)
    {
        Query = query;
        Source = source;
    }

    /// <summary>The rewritten query.</summary>
    public Query Query { get; }

    /// <summary>The source to evaluate it over.</summary>
    public IQuadSource Source { get; }
}

/// <summary>
/// Rewrites a query's basic graph patterns so that simple evaluation over the
/// merge answers what the regime entails (ADR 0193): complete for the RDF,
/// RDFS and D regimes under their answer conditions.
/// </summary>
[Contract(typeof(RdfsRdfAndDByRewriting.CompleteForTheThreeRegimes), Role = "the entry point of tier 1")]
public static class EntailmentRewriter
{
    /// <summary>
    /// <paramref name="query"/> under <paramref name="regime"/>, reading
    /// <paramref name="source"/> — the request's pinned, scoped view — with
    /// each graph merged with the TBox graphs of the one of
    /// <paramref name="covers"/> that covers it. Under simple entailment, the
    /// query and the source as given.
    /// </summary>
    /// <remarks>
    /// <paramref name="recognisedDatatypes"/> is RDF 1.1 Semantics' D, beside
    /// <c>xsd:string</c> and <c>rdf:langString</c>, which every regime
    /// recognises. Null is the regime's own: none more under RDF and RDFS,
    /// Varve.Xsd's map under D. A datatype outside Varve.Xsd's map is
    /// recognised by name only.
    /// </remarks>
    /// <remarks>
    /// <paramref name="schemas"/> keeps each scoping graph's schema across
    /// requests, keyed by the versions of the graphs it merges, where
    /// <paramref name="source"/> names them (<see cref="IGraphVersionSource"/>);
    /// one cache serves one dataset. Null saturates every schema per request.
    /// </remarks>
    /// <remarks>
    /// A graph is covered by at most one binding (ADR 0194 point 2); were two
    /// to cover it, the first would. A graph none covers is read over its own
    /// triples, its TBox empty.
    /// </remarks>
    public static EntailedQuery Rewrite(Query query, IQuadSource source, IReadOnlyList<TBoxCover> covers, EntailmentRegime regime, IReadOnlyCollection<RdfTerm>? recognisedDatatypes = null, SchemaCache? schemas = null, CancellationToken cancellationToken = default) =>
        Rewrite(query, source, covers, regime, Pruning.On, recognisedDatatypes, schemas, cancellationToken);

    /// <summary>The rewriting with its pruning chosen: off for the property that pruning changes no answer.</summary>
    internal static EntailedQuery Rewrite(Query query, IQuadSource source, IReadOnlyList<TBoxCover> covers, EntailmentRegime regime, Pruning pruning, IReadOnlyCollection<RdfTerm>? recognisedDatatypes = null, SchemaCache? schemas = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(covers);
        if (regime == EntailmentRegime.Simple)
        {
            return new EntailedQuery(query, source);
        }

        TBoxMerge merge = new(source, covers);
        RuleSet rules = recognisedDatatypes is null ? RulesOf(regime) : RulesOf(regime, recognisedDatatypes);
        Rewriter rewriter = new(merge, query.Dataset, rules, NamedMembership.Of(query), schemas, cancellationToken) { Pruning = pruning };
        return new EntailedQuery(rewriter.Rewrite(query), merge);
    }

    /// <summary>
    /// The graphs of <paramref name="source"/> whose content makes a schema
    /// under RDFS or D read its scoping graph whole (ADR 0193 point 2): each
    /// holds a triple that says something of the RDF or RDFS vocabulary
    /// itself. Found by the saturation's own probes, through
    /// <paramref name="schemas"/> where given, so a scoping graph whose
    /// versions have not changed is not read again.
    /// </summary>
    /// <remarks>
    /// The scoping graphs asked are those a query can meet: the default
    /// graph, each binding's TBox graphs, and each named graph with schema
    /// triples of its own. A graph named here forces the full read of every
    /// scoping graph it is part of: a data graph its own, a TBox graph every
    /// graph its binding covers.
    /// </remarks>
    public static FullReadGraphs FullReads(IQuadSource source, IReadOnlyList<TBoxCover> covers, SchemaCache? schemas = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(covers);
        TBoxMerge merge = new(source, covers);
        return new Rewriter(merge, null, RulesOf(EntailmentRegime.Rdfs), [], schemas, cancellationToken).FullReads();
    }

    /// <summary>A regime's rules, recognising <paramref name="recognised"/> beside the two every regime does.</summary>
    internal static RuleSet RulesOf(EntailmentRegime regime, IEnumerable<RdfTerm> recognised)
    {
        HashSet<RdfTerm> datatypes = new(Datatypes.RdfRecognised, RdfTerm.Comparer);
        foreach (RdfTerm datatype in recognised)
        {
            datatypes.Add(datatype);
        }

        return new RuleSet(regime != EntailmentRegime.Rdf, datatypes);
    }

    /// <summary>The rules and recognised datatypes of a regime.</summary>
    internal static RuleSet RulesOf(EntailmentRegime regime) => regime switch
    {
        EntailmentRegime.Rdf => new RuleSet(rdfs: false, Datatypes.RdfRecognised),
        EntailmentRegime.Rdfs => new RuleSet(rdfs: true, Datatypes.RdfRecognised),
        EntailmentRegime.D => new RuleSet(rdfs: true, Datatypes.DatatypeMap),
        _ => throw new ArgumentOutOfRangeException(nameof(regime), regime, "Simple entailment is not rewritten."),
    };

    // Each basic graph pattern under the schema of its scoping graph: the
    // dataset's default graph, a named graph under GRAPH, or, under GRAPH ?g,
    // each named graph with schema triples of its own and one branch for the
    // rest (ADR 0193 point 2).
    private sealed class Rewriter : AlgebraRewriter
    {
        private readonly TBoxMerge _merge;
        private readonly RuleSet _rules;
        private readonly IReadOnlyCollection<RdfTerm> _named;
        private readonly SchemaCache? _cache;

        internal Pruning Pruning { get; init; }
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<string, PatternRewriting> _compiled = [];
        private readonly List<TermHandle>? _namedGraphs;
        private IReadOnlyList<GraphPattern> _scope;
        private bool _exact = true;

        internal Rewriter(TBoxMerge merge, DatasetSpec? dataset, RuleSet rules, IReadOnlyCollection<RdfTerm> named, SchemaCache? cache, CancellationToken cancellationToken)
        {
            _merge = merge;
            _rules = rules;
            _named = named;
            _cache = cache;
            _cancellationToken = cancellationToken;
            if (dataset is null)
            {
                _scope = [GraphPattern.DefaultGraph];
                return;
            }

            List<GraphPattern> from = [];
            foreach (RdfTerm graph in dataset.DefaultGraphs)
            {
                if (merge.TryInternalise(graph, out TermHandle handle))
                {
                    from.Add(GraphPattern.Named(handle));
                }
            }

            _scope = from;
            _namedGraphs = [];
            foreach (RdfTerm graph in dataset.NamedGraphs)
            {
                if (merge.TryInternalise(graph, out TermHandle handle))
                {
                    _namedGraphs.Add(handle);
                }
            }
        }

        protected override QueryPattern RewriteBgp(Bgp pattern) => Compiled(_scope, _exact).Rewrite(pattern);

        // Property paths are evaluated without entailment (§10), and a
        // SERVICE's pattern is the remote endpoint's.
        protected override QueryPattern RewritePathPattern(PathPattern pattern) => pattern;

        protected override QueryPattern RewriteService(Service pattern) => pattern;

        protected override QueryPattern RewriteGraph(Graph pattern)
        {
            switch (pattern.Name)
            {
                case TermPattern { Term: var name }:
                    if (!_merge.TryInternalise(name, out TermHandle handle))
                    {
                        return pattern;
                    }

                    return new Graph(pattern.Name, Within([GraphPattern.Named(handle)], pattern.Inner));
                case VariablePattern { Variable: var g }:
                    return ByGraph(g, pattern);
                default:
                    return pattern;
            }
        }

        // The named graphs partitioned by the Σ they meet: each with schema
        // triples of its own, then each binding's covered rest, then the rest
        // no binding covers, under the axioms alone (ADR 0193 point 2).
        private QueryPattern ByGraph(Variable g, Graph pattern)
        {
            List<TermHandle> own = OwnSchemaGraphs();
            HashSet<TermHandle> owned = new(own, _merge.TermComparer);
            QueryPattern? union = null;
            foreach (TermHandle graph in own)
            {
                Add([graph], [GraphPattern.Named(graph)]);
            }

            foreach (CoverIndex cover in _merge.Covers())
            {
                Group(_merge.CoveredNamedGraphs(cover).FindAll(graph => !owned.Contains(graph)), TBoxOf(cover));
            }

            Group(_merge.NamedGraphs().FindAll(graph => !owned.Contains(graph) && !_merge.TryGetCover(graph, out _)), []);
            return union ?? new Values(AlgebraList.Of(g), AlgebraList.Empty<AlgebraList<RdfTerm?>>());

            // A group shares one rewriting, under its TBox, but C2 asks each
            // graph whether it names a term: a graph that names a candidate
            // the TBox does not is rewritten on its own (ADR 0193 point 4).
            void Group(List<TermHandle> graphs, IReadOnlyList<GraphPattern> scope)
            {
                PatternRewriting shared = Compiled(scope, exact: false);
                string key = KeyOf(scope);
                HashSet<TermHandle> apart = new(_merge.TermComparer);
                foreach (RdfTerm term in shared.Candidates(_named))
                {
                    if (!Occurs(scope, key, term))
                    {
                        apart.UnionWith(GraphsNaming(term));
                    }
                }

                List<TermHandle> together = [];
                foreach (TermHandle graph in graphs)
                {
                    if (apart.Contains(graph))
                    {
                        Add([graph], [GraphPattern.Named(graph)]);
                    }
                    else
                    {
                        together.Add(graph);
                    }
                }

                Add(together, scope, exact: false);
            }

            // A graph rewritten on its own is its scoping graph, and its
            // distinct terms are its own; a group's are not (ADR 0193).
            void Add(List<TermHandle> graphs, IReadOnlyList<GraphPattern> scope, bool exact = true)
            {
                List<AlgebraList<RdfTerm?>> rows = [];
                foreach (TermHandle graph in graphs)
                {
                    if (_merge.TryExternalise(graph, out RdfTerm? name))
                    {
                        rows.Add(AlgebraList.Of<RdfTerm?>(name));
                    }
                }

                if (rows.Count == 0)
                {
                    return;
                }

                QueryPattern one = new Join(
                    new Values(AlgebraList.Of(g), AlgebraList.From(rows)),
                    new Graph(pattern.Name, Within(scope, pattern.Inner, exact)));
                union = union is null ? one : new Union(union, one);
            }
        }

        private QueryPattern Within(IReadOnlyList<GraphPattern> scope, QueryPattern inner, bool exact = true)
        {
            IReadOnlyList<GraphPattern> outer = _scope;
            bool outerExact = _exact;
            _scope = scope;
            _exact = exact;
            try
            {
                return Rewrite(inner);
            }
            finally
            {
                _scope = outer;
                _exact = outerExact;
            }
        }

        // The scoping graph of a covered named graph with no schema of its own: its binding's TBox.
        private List<GraphPattern> TBoxOf(CoverIndex cover)
        {
            List<GraphPattern> tbox = [];
            foreach (TermHandle graph in _merge.TBoxGraphsOf(cover))
            {
                tbox.Add(GraphPattern.Named(graph));
            }

            return tbox;
        }

        // The named graphs holding schema triples of their own, found by each
        // TBox schema's predicates, and the axioms', over every named graph,
        // unmerged.
        private List<TermHandle> OwnSchemaGraphs()
        {
            IQuadSource inner = _merge.Inner;
            HashSet<TermHandle> graphs = new(inner.TermComparer);
            List<TermHandle> found = [];
            HashSet<Scan> probes = [];
            List<List<GraphPattern>> scopes = [[]];
            foreach (CoverIndex cover in _merge.Covers())
            {
                scopes.Add(TBoxOf(cover));
            }

            // The scans each TBox's saturation read: a graph that matches
            // none saturates to its TBox's Σ (ADR 0193 point 2).
            foreach (List<GraphPattern> scope in scopes)
            {
                probes.UnionWith(SchemaOf(scope).Scans);
            }

            foreach ((RdfTerm? s, RdfTerm? p, RdfTerm? o) in probes)
            {
                TermHandle sh = TermHandle.None;
                if (s is not null && !inner.TryInternalise(s, out sh))
                {
                    continue;
                }

                TermHandle ph = TermHandle.None;
                if (p is not null && !inner.TryInternalise(p, out ph))
                {
                    continue;
                }

                TermHandle oh = TermHandle.None;
                if (o is not null && !inner.TryInternalise(o, out oh))
                {
                    continue;
                }

                using IQuadCursor cursor = inner.Match(sh, ph, oh, GraphPattern.AnyNamed);
                while (cursor.MoveNext())
                {
                    TermHandle graph = cursor.Current.Graph;
                    if (graphs.Add(graph) && (_namedGraphs is null || _namedGraphs.Exists(n => inner.TermComparer.Equals(n, graph))))
                    {
                        found.Add(graph);
                    }
                }
            }

            return found;
        }

        // Every scoping graph a query can meet, saturated, and the graphs that
        // hold the triples that made one read whole.
        internal FullReadGraphs FullReads()
        {
            List<IReadOnlyList<GraphPattern>> scopes = [[GraphPattern.DefaultGraph]];
            foreach (CoverIndex cover in _merge.Covers())
            {
                if (TBoxOf(cover) is { Count: > 0 } tbox)
                {
                    scopes.Add(tbox);
                }
            }

            foreach (TermHandle graph in OwnSchemaGraphs())
            {
                scopes.Add([GraphPattern.Named(graph)]);
            }

            IQuadSource inner = _merge.Inner;
            bool inDefault = false;
            HashSet<TermHandle> named = new(inner.TermComparer);
            foreach (IReadOnlyList<GraphPattern> scope in scopes)
            {
                foreach (Triple cause in SchemaOf(scope).FullReadCauses)
                {
                    if (!inner.TryInternalise(cause.S, out TermHandle s) || !inner.TryInternalise(cause.P, out TermHandle p) || !inner.TryInternalise(cause.O, out TermHandle o))
                    {
                        continue;
                    }

                    using IQuadCursor cursor = inner.Match(s, p, o, GraphPattern.Any);
                    while (cursor.MoveNext())
                    {
                        if (cursor.Current.Graph.IsNone)
                        {
                            inDefault = true;
                        }
                        else
                        {
                            named.Add(cursor.Current.Graph);
                        }
                    }
                }
            }

            List<RdfTerm> names = [];
            foreach (TermHandle graph in named)
            {
                if (inner.TryExternalise(graph, out RdfTerm? name))
                {
                    names.Add(name);
                }
            }

            names.Sort(static (a, b) => string.CompareOrdinal(Text(a), Text(b)));
            List<RdfTerm?> graphs = inDefault ? [null] : [];
            graphs.AddRange(names);
            return new FullReadGraphs(graphs);
        }

        // A scoping graph that is one set of graphs is exact; a group under
        // GRAPH ?g shares a TBox but not its graphs' terms.
        private PatternRewriting Compiled(IReadOnlyList<GraphPattern> scope, bool exact = true)
        {
            string key = KeyOf(scope) + (exact ? string.Empty : "|group");
            if (!_compiled.TryGetValue(key, out PatternRewriting? compiled))
            {
                _compiled[key] = compiled = new PatternRewriting(SchemaOf(scope), term => Occurs(scope, key, term), exact ? predicate => Distinct(scope, key, predicate) : null, Pruning);
            }

            return compiled;
        }

        private readonly Dictionary<string, Schema> _schemas = [];
        private readonly Dictionary<(string Scope, RdfTerm Term), bool> _occurs = [];
        private readonly Dictionary<(string Scope, RdfTerm? Predicate), IReadOnlyList<RdfTerm>> _distinct = [];

        // The distinct predicates of the scoping graph, or the distinct
        // objects of a predicate in it: by each graph's seek, through the
        // merge, once per request (ADR 0193).
        private IReadOnlyList<RdfTerm> Distinct(IReadOnlyList<GraphPattern> scope, string key, RdfTerm? predicate)
        {
            if (_distinct.TryGetValue((key, predicate), out IReadOnlyList<RdfTerm>? known))
            {
                return known;
            }

            List<RdfTerm> terms = [];
            HashSet<RdfTerm> seen = new(RdfTerm.Comparer);
            TermHandle handle = TermHandle.None;
            if (predicate is null || _merge.TryInternalise(predicate, out handle))
            {
                foreach (GraphPattern graph in scope)
                {
                    foreach (TermHandle term in predicate is null ? DistinctTerms.Predicates(_merge, graph) : DistinctTerms.Objects(_merge, handle, graph))
                    {
                        if (_merge.TryExternalise(term, out RdfTerm? external) && seen.Add(external))
                        {
                            terms.Add(external);
                        }
                    }
                }
            }

            _distinct[(key, predicate)] = terms;
            return terms;
        }

        // Whether the scoping graph, through the merge, names a term: three
        // bound probes per graph, once per request (ADR 0193 point 4).
        private bool Occurs(IReadOnlyList<GraphPattern> scope, string key, RdfTerm term)
        {
            if (_occurs.TryGetValue((key, term), out bool known))
            {
                return known;
            }

            bool found = false;
            if (_merge.TryInternalise(term, out TermHandle handle))
            {
                foreach (GraphPattern graph in scope)
                {
                    found = Any(_merge, handle, TermHandle.None, TermHandle.None, graph)
                        || Any(_merge, TermHandle.None, handle, TermHandle.None, graph)
                        || Any(_merge, TermHandle.None, TermHandle.None, handle, graph);
                    if (found)
                    {
                        break;
                    }
                }
            }

            _occurs[(key, term)] = found;
            return found;
        }

        private static bool Any(TBoxMerge source, TermHandle s, TermHandle p, TermHandle o, GraphPattern graph)
        {
            using IQuadCursor cursor = source.Match(s, p, o, graph);
            return cursor.MoveNext();
        }

        // The named graphs that name a term, unmerged.
        private HashSet<TermHandle> GraphsNaming(RdfTerm term)
        {
            IQuadSource inner = _merge.Inner;
            HashSet<TermHandle> graphs = new(inner.TermComparer);
            if (inner.TryInternalise(term, out TermHandle handle))
            {
                foreach ((TermHandle s, TermHandle p, TermHandle o) in (ReadOnlySpan<(TermHandle, TermHandle, TermHandle)>)[(handle, TermHandle.None, TermHandle.None), (TermHandle.None, handle, TermHandle.None), (TermHandle.None, TermHandle.None, handle)])
                {
                    using IQuadCursor cursor = inner.Match(s, p, o, GraphPattern.AnyNamed);
                    while (cursor.MoveNext())
                    {
                        graphs.Add(cursor.Current.Graph);
                    }
                }
            }

            return graphs;
        }

        private Schema SchemaOf(IReadOnlyList<GraphPattern> scope)
        {
            string key = KeyOf(scope);
            if (_schemas.TryGetValue(key, out Schema? schema))
            {
                return schema;
            }

            SchemaKey? versioned = _cache is null ? null : VersionsOf(scope);
            if (versioned is not { } cached || !_cache!.TryGet(cached, out schema))
            {
                schema = Schema.Saturate(_merge, scope, _rules, _named, _cancellationToken);
                if (versioned is { } kept)
                {
                    _cache!.Add(kept, schema);
                }
                else
                {
                    _cache?.Missed();
                }
            }

            _schemas[key] = schema;
            return schema;
        }

        // What a scoping graph's schema depends on: the regime's rules, the
        // rdf:_n the query names, and each graph it merges, its own and its
        // TBox graphs, by name and version; null where the source cannot name
        // a version (ADR 0193).
        private SchemaKey? VersionsOf(IReadOnlyList<GraphPattern> scope)
        {
            if (_merge.Inner is not IGraphVersionSource versions)
            {
                return null;
            }

            List<string> rules = [_rules.Rdfs ? "rdfs" : "rdf"];
            foreach (RdfTerm datatype in _rules.Recognised)
            {
                rules.Add(Text(datatype));
            }

            List<string> named = [];
            foreach (RdfTerm term in _named)
            {
                named.Add(Text(term));
            }

            List<string> graphs = [];
            foreach (GraphPattern graph in scope)
            {
                if (Version(graph) is not { } own)
                {
                    return null;
                }

                System.Text.StringBuilder read = new(own);
                foreach (TermHandle tbox in _merge.TBoxGraphsOf(graph))
                {
                    if (Version(GraphPattern.Named(tbox)) is not { } merged)
                    {
                        return null;
                    }

                    read.Append(" + ").Append(merged);
                }

                graphs.Add(read.ToString());
            }

            rules.Sort(StringComparer.Ordinal);
            named.Sort(StringComparer.Ordinal);
            graphs.Sort(StringComparer.Ordinal);
            return new SchemaKey(string.Join(" ", rules) + " | " + string.Join(" ", named) + " | " + string.Join(" ; ", graphs));

            string? Version(GraphPattern graph)
            {
                string name;
                if (graph.Match == GraphMatch.DefaultGraph)
                {
                    name = "default";
                }
                else if (_merge.TryExternalise(graph.Graph, out RdfTerm? term))
                {
                    name = Text(term);
                }
                else
                {
                    return null;
                }

                return versions.TryGetGraphVersion(graph, out GraphVersion version)
                    ? name + "@" + version.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : null;
            }
        }

        // A term in a key: an IRI or a blank node, as graphs, datatypes and
        // rdf:_n are.
        private static string Text(RdfTerm term) =>
            (term.Kind == RdfTermKind.BlankNode ? "_:" : "<") + System.Text.Encoding.UTF8.GetString(term.Lexical) + (term.Kind == RdfTermKind.BlankNode ? string.Empty : ">");

        private static string KeyOf(IReadOnlyList<GraphPattern> scope)
        {
            List<string> parts = [];
            foreach (GraphPattern graph in scope)
            {
                parts.Add(graph.Match + ":" + graph.Graph.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts);
        }
    }

    // The rdf:_n a query's basic graph patterns name.
    private sealed class NamedMembership : AlgebraRewriter
    {
        private readonly HashSet<RdfTerm> _found = new(RdfTerm.Comparer);

        internal static HashSet<RdfTerm> Of(Query query)
        {
            NamedMembership walk = new();
            walk.Rewrite(query);
            return walk._found;
        }

        protected override QueryPattern RewriteBgp(Bgp pattern)
        {
            foreach (TriplePattern triple in pattern.Triples)
            {
                foreach (PatternTerm term in (PatternTerm[])[triple.Subject, triple.Predicate, triple.Object])
                {
                    if (term is TermPattern { Term: var named } && Vocabulary.IsContainerMembership(named))
                    {
                        _found.Add(named);
                    }
                }
            }

            return pattern;
        }
    }
}
