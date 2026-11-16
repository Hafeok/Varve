// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference.Store;

/// <summary>
/// A binding's closures at one position (ADR 0195 points 2 and 3): one of the
/// TBox graphs' quads, and one per data graph of its own quads and the TBox
/// graphs', every one taking its facts from one budget.
/// </summary>
/// <remarks>
/// A fact is a row of the TBox conclusions' graph when the TBox closure
/// derives it and it is not explicit there; it is a row of a data graph when
/// that graph's closure derives it, it is not explicit there, and the TBox
/// closure does not hold it. Only a well-formed fact is a row: a literal
/// subject or a predicate that is not an IRI is held for the joins and never
/// served.
/// </remarks>
[Contract(typeof(TheInferenceProjection.PlacedByPremiseSets), Role = "a binding's closures, placed by graph")]
internal sealed class Materialisation
{
    private readonly InferenceProjection _projection;
    private readonly Closure _tbox;
    private readonly Dictionary<TermHandle, Closure> _graphs = [];
    private readonly HashSet<Fact> _tboxExplicit = [];
    private readonly Dictionary<TermHandle, bool> _data = [];
    private readonly Dictionary<TermHandle, bool> _tboxGraphs = [];
    private readonly List<TermHandle> _dropped = [];

    private Materialisation(ProjectionTerms terms, InferenceProjection projection)
    {
        _projection = projection;
        Terms = terms;
        terms.BeginProgram();
        Budget = new FactBudget(projection.FactLimit);
        _tbox = new Closure(projection.Binding.Profile, Terms, Budget);
        TBoxGraph = Terms.Handle(projection.TBoxGraph);
    }

    internal ProjectionTerms Terms { get; }

    internal FactBudget Budget { get; }

    /// <summary>The handle the TBox conclusions' graph's rows are placed under.</summary>
    internal TermHandle TBoxGraph { get; }

    /// <summary>The closures of <paramref name="source"/>'s quads, from scratch, over <paramref name="terms"/>, whose source it becomes.</summary>
    /// <exception cref="ClosureLimitExceededException">They would pass the binding's limit; nothing is kept.</exception>
    internal static Materialisation Build(IQuadSource source, ProjectionTerms terms, InferenceProjection projection, CancellationToken cancellationToken)
    {
        terms.Source = source;
        Materialisation built = new(terms, projection);
        try
        {
            List<Fact> tbox = [];
            Dictionary<TermHandle, List<Fact>> graphs = [];
            using (IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
            {
                while (cursor.MoveNext())
                {
                    Quad quad = cursor.Current;
                    Fact fact = new(quad.Subject, quad.Predicate, quad.Object);
                    if (built.IsTBox(source, quad.Graph))
                    {
                        tbox.Add(fact);
                    }
                    else if (built.IsData(source, quad.Graph))
                    {
                        if (!graphs.TryGetValue(quad.Graph, out List<Fact>? facts))
                        {
                            graphs[quad.Graph] = facts = [];
                        }

                        facts.Add(fact);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            built._tbox.Apply([.. tbox], []);
            built._tboxExplicit.UnionWith(tbox);
            foreach ((TermHandle graph, List<Fact> facts) in graphs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Closure closure = new(projection.Binding.Profile, built.Terms, built.Budget);
                built._graphs[graph] = closure;
                closure.Apply([.. tbox, .. facts], []);
            }

            return built;
        }
        catch
        {
            built.Release();
            throw;
        }
    }

    /// <summary>
    /// Applies a commit's exact delta: the TBox graphs' part to every closure,
    /// each data graph's to its own. Answers, per graph, the facts whose row
    /// may have changed.
    /// </summary>
    /// <exception cref="ClosureLimitExceededException">A closure would pass the binding's limit.</exception>
    internal Dictionary<TermHandle, HashSet<Fact>> Apply(IQuadSource view, QuadDelta delta)
    {
        List<Fact> tboxAsserted = [], tboxRetracted = [];
        Dictionary<TermHandle, (List<Fact> Asserted, List<Fact> Retracted)> graphs = [];
        Split(view, delta.Asserted, tboxAsserted, graphs, asserted: true);
        Split(view, delta.Retracted, tboxRetracted, graphs, asserted: false);

        Dictionary<TermHandle, HashSet<Fact>> changed = [];
        if (tboxAsserted.Count > 0 || tboxRetracted.Count > 0)
        {
            IReadOnlyCollection<Fact> tboxChanged = _tbox.Apply([.. tboxAsserted], [.. tboxRetracted]);
            _tboxExplicit.ExceptWith(tboxRetracted);
            _tboxExplicit.UnionWith(tboxAsserted);
            HashSet<Fact> tboxRows = Into(changed, TBoxGraph);
            tboxRows.UnionWith(tboxChanged);

            // A triple asserted that was implicit, or retracted and still
            // entailed, changes its row but not the closure's membership.
            tboxRows.UnionWith(tboxAsserted);
            tboxRows.UnionWith(tboxRetracted);

            foreach ((TermHandle graph, Closure closure) in _graphs)
            {
                // A TBox triple the graph asserts too stays explicit there.
                List<Fact> gone = [];
                foreach (Fact fact in tboxRetracted)
                {
                    if (!view.Contains(new Quad(fact.Subject, fact.Predicate, fact.Object, graph)))
                    {
                        gone.Add(fact);
                    }
                }

                HashSet<Fact> into = Into(changed, graph);
                into.UnionWith(closure.Apply([.. tboxAsserted], [.. gone]));
                into.UnionWith(tboxChanged);
                into.UnionWith(tboxAsserted);
                into.UnionWith(gone);
            }
        }

        foreach ((TermHandle graph, (List<Fact> asserted, List<Fact> retracted)) in graphs)
        {
            HashSet<Fact> into = Into(changed, graph);
            if (!_graphs.TryGetValue(graph, out Closure? closure))
            {
                closure = new Closure(_projection.Binding.Profile, Terms, Budget);
                _graphs[graph] = closure;
                into.UnionWith(closure.Apply([.. _tboxExplicit], []));
            }

            // A triple the TBox graphs assert too stays explicit in every closure.
            List<Fact> gone = [];
            foreach (Fact fact in retracted)
            {
                if (!_tboxExplicit.Contains(fact))
                {
                    gone.Add(fact);
                }
            }

            into.UnionWith(closure.Apply([.. asserted], [.. gone]));
            into.UnionWith(asserted);
            into.UnionWith(retracted);

            if (retracted.Count > 0 && !HasQuads(view, graph))
            {
                closure.Release();
                _graphs.Remove(graph);
                _dropped.Add(graph);
            }
        }

        return changed;
    }

    /// <summary>The data graphs whose closures were dropped since the last call, every quad of theirs retracted.</summary>
    internal List<TermHandle> TakeDropped()
    {
        List<TermHandle> dropped = [.. _dropped];
        _dropped.Clear();
        return dropped;
    }

    /// <summary>Whether a fact is a row of a graph now.</summary>
    internal bool IsRow(TermHandle graph, in Fact fact)
    {
        if (graph == TBoxGraph)
        {
            return _tbox.Contains(fact) && !_tbox.IsExplicit(fact) && Servable(fact);
        }

        return _graphs.TryGetValue(graph, out Closure? closure)
            && closure.Contains(fact) && !closure.IsExplicit(fact) && !_tbox.Contains(fact) && Servable(fact);
    }

    /// <summary>Every row now.</summary>
    internal IEnumerable<(TermHandle Graph, Fact Fact)> Rows()
    {
        foreach (Fact fact in _tbox.Implicit)
        {
            if (Servable(fact))
            {
                yield return (TBoxGraph, fact);
            }
        }

        foreach ((TermHandle graph, Closure closure) in _graphs)
        {
            foreach (Fact fact in closure.Implicit)
            {
                if (!_tbox.Contains(fact) && Servable(fact))
                {
                    yield return (graph, fact);
                }
            }
        }
    }

    /// <summary>The clashes now: the TBox closure's under its graph, each data graph's that the TBox closure does not hold under that graph.</summary>
    internal IReadOnlyList<PlacedClash> Clashes()
    {
        List<PlacedClash> clashes = [];
        foreach (Clash clash in _tbox.Clashes())
        {
            clashes.Add(new PlacedClash(TBoxGraph, clash));
        }

        foreach ((TermHandle graph, Closure closure) in _graphs)
        {
            foreach (Clash clash in closure.Clashes())
            {
                bool tboxOnly = true;
                foreach (Fact premise in clash.Premises)
                {
                    tboxOnly &= _tbox.Contains(premise);
                }

                if (!tboxOnly)
                {
                    clashes.Add(new PlacedClash(graph, clash));
                }
            }
        }

        return clashes;
    }

    /// <summary>Gives every closure's facts back to the budget.</summary>
    internal void Release()
    {
        _tbox.Release();
        foreach (Closure closure in _graphs.Values)
        {
            closure.Release();
        }

        _graphs.Clear();
    }

    private bool Servable(in Fact fact) => !Terms.IsLiteral(fact.Subject) && Terms.IsIri(fact.Predicate);

    private static HashSet<Fact> Into(Dictionary<TermHandle, HashSet<Fact>> changed, TermHandle graph)
    {
        if (!changed.TryGetValue(graph, out HashSet<Fact>? facts))
        {
            changed[graph] = facts = [];
        }

        return facts;
    }

    private void Split(IQuadSource view, System.ReadOnlySpan<Quad> quads, List<Fact> tbox, Dictionary<TermHandle, (List<Fact> Asserted, List<Fact> Retracted)> graphs, bool asserted)
    {
        foreach (Quad quad in quads)
        {
            Fact fact = new(quad.Subject, quad.Predicate, quad.Object);
            if (IsTBox(view, quad.Graph))
            {
                tbox.Add(fact);
            }
            else if (IsData(view, quad.Graph))
            {
                if (!graphs.TryGetValue(quad.Graph, out (List<Fact> Asserted, List<Fact> Retracted) lists))
                {
                    graphs[quad.Graph] = lists = ([], []);
                }

                (asserted ? lists.Asserted : lists.Retracted).Add(fact);
            }
        }
    }

    private static bool HasQuads(IQuadSource view, TermHandle graph)
    {
        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, graph.IsNone ? GraphPattern.DefaultGraph : GraphPattern.Named(graph));
        return cursor.MoveNext();
    }

    private bool IsTBox(IQuadSource source, TermHandle graph)
    {
        if (graph.IsNone)
        {
            return false;
        }

        if (!_tboxGraphs.TryGetValue(graph, out bool tbox))
        {
            tbox = false;
            if (source.TryExternalise(graph, out RdfTerm? term))
            {
                foreach (RdfTerm bound in _projection.Binding.TBoxGraphs)
                {
                    tbox |= bound.Equals(term);
                }
            }

            _tboxGraphs[graph] = tbox;
        }

        return tbox;
    }

    private bool IsData(IQuadSource source, TermHandle graph)
    {
        if (!_data.TryGetValue(graph, out bool data))
        {
            data = graph.IsNone
                ? _projection.IsData(null)
                : source.TryExternalise(graph, out RdfTerm? term) && _projection.IsData(term);
            _data[graph] = data;
        }

        return data;
    }
}
