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
    /// A graph is covered by at most one binding (ADR 0194 point 2); were two
    /// to cover it, the first would. A graph none covers is read over its own
    /// triples, its TBox empty.
    /// </remarks>
    public static EntailedQuery Rewrite(Query query, IQuadSource source, IReadOnlyList<TBoxCover> covers, EntailmentRegime regime, IReadOnlyCollection<RdfTerm>? recognisedDatatypes = null, CancellationToken cancellationToken = default)
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
        Rewriter rewriter = new(merge, query.Dataset, rules, cancellationToken);
        return new EntailedQuery(rewriter.Rewrite(query), merge);
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
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<string, PatternRewriting> _compiled = [];
        private readonly List<TermHandle>? _namedGraphs;
        private IReadOnlyList<GraphPattern> _scope;

        internal Rewriter(TBoxMerge merge, DatasetSpec? dataset, RuleSet rules, CancellationToken cancellationToken)
        {
            _merge = merge;
            _rules = rules;
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

        protected override QueryPattern RewriteBgp(Bgp pattern) => Compiled(_scope).Rewrite(pattern);

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
                Add(_merge.CoveredNamedGraphs(cover).FindAll(graph => !owned.Contains(graph)), TBoxOf(cover));
            }

            Add(_merge.NamedGraphs().FindAll(graph => !owned.Contains(graph) && !_merge.TryGetCover(graph, out _)), []);
            return union ?? new Values(AlgebraList.Of(g), AlgebraList.Empty<AlgebraList<RdfTerm?>>());

            void Add(List<TermHandle> graphs, IReadOnlyList<GraphPattern> scope)
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
                    new Graph(pattern.Name, Within(scope, pattern.Inner)));
                union = union is null ? one : new Union(union, one);
            }
        }

        private QueryPattern Within(IReadOnlyList<GraphPattern> scope, QueryPattern inner)
        {
            IReadOnlyList<GraphPattern> outer = _scope;
            _scope = scope;
            try
            {
                return Rewrite(inner);
            }
            finally
            {
                _scope = outer;
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
            HashSet<(RdfTerm P, RdfTerm? O)> probes = [];
            List<List<GraphPattern>> scopes = [[]];
            foreach (CoverIndex cover in _merge.Covers())
            {
                scopes.Add(TBoxOf(cover));
            }

            foreach (List<GraphPattern> scope in scopes)
            {
                Schema tbox = SchemaOf(scope);
                foreach (RdfTerm predicate in tbox.SchemaPredicates)
                {
                    probes.Add((predicate, null));
                }

                foreach (RdfTerm meta in tbox.MetaClasses())
                {
                    foreach (RdfTerm type in tbox.TypeLike)
                    {
                        probes.Add((type, meta));
                    }
                }
            }

            foreach ((RdfTerm p, RdfTerm? o) in probes)
            {
                if (!inner.TryInternalise(p, out TermHandle ph))
                {
                    continue;
                }

                TermHandle oh = TermHandle.None;
                if (o is not null && !inner.TryInternalise(o, out oh))
                {
                    continue;
                }

                using IQuadCursor cursor = inner.Match(TermHandle.None, ph, oh, GraphPattern.AnyNamed);
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

        private PatternRewriting Compiled(IReadOnlyList<GraphPattern> scope)
        {
            string key = KeyOf(scope);
            if (!_compiled.TryGetValue(key, out PatternRewriting? compiled))
            {
                _compiled[key] = compiled = new PatternRewriting(SchemaOf(scope));
            }

            return compiled;
        }

        private readonly Dictionary<string, Schema> _schemas = [];

        private Schema SchemaOf(IReadOnlyList<GraphPattern> scope)
        {
            string key = KeyOf(scope);
            if (!_schemas.TryGetValue(key, out Schema? schema))
            {
                _schemas[key] = schema = Schema.Saturate(_merge, scope, _rules, _cancellationToken);
            }

            return schema;
        }

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
}
