// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Inference.Store.Model;
using Varve.Rdf;

namespace Varve.Inference.Store;

/// <summary>
/// A binding's closures at one position (ADR 0195 points 2 and 3): one of the
/// TBox graphs' quads, and one per data graph of its own quads over it, the
/// TBox closure's facts shared beneath every one, all taking their facts
/// from one budget. A TBox change rebuilds them all: a generation.
/// </summary>
/// <remarks>
/// A fact is a row of the TBox conclusions' graph when the TBox closure
/// derives it and it is not explicit there; it is a row of a data graph when
/// that graph's closure derives it, it is not explicit there, and the TBox
/// closure does not hold it. Only a well-formed fact is a row: a literal
/// subject or a predicate that is not an IRI is held for the joins and never
/// served.
/// </remarks>
/// <summary>
/// A binding's closures at one position (ADR 0195 points 2 and 3), and where
/// each row sits (ADR 0190 points 2 and 6): the TBox graphs' closure, under
/// the data graphs' closures, all taking their facts from one budget. A TBox
/// change rebuilds them all: a generation. An intra-graph binding's are
/// <see cref="PerGraphMaterialisation"/>, a cross-graph binding's
/// <see cref="CrossGraphMaterialisation"/>.
/// </summary>
/// <remarks>
/// A TBox conclusion is a row of the TBox conclusions' graph when the TBox
/// closure derives it and it is not explicit there. Only a well-formed fact
/// is a row: a literal subject or a predicate that is not an IRI is held for
/// the joins and never served.
/// </remarks>
[Contract(typeof(TheInferenceProjection.PlacedByPremiseSets), Role = "a binding's closures, placed by graph")]
internal abstract class Materialisation
{
    private readonly Dictionary<TermHandle, bool> _data = [];
    private readonly Dictionary<TermHandle, bool> _tboxGraphs = [];
    private readonly List<TermHandle> _dropped = [];

    // What the terms this materialisation keeps resolved have taken from its budget.
    private long _termCharge;

    protected Materialisation(ProjectionTerms terms, InferenceProjection projection)
    {
        Projection = projection;
        Terms = terms;
        terms.BeginProgram();
        Budget = projection.NewBudget();
        MemoryAmount known = terms.KnownBytes(InferenceOptions.TermBytes);
        Budget.TakeBeside(known);
        _termCharge = known.Value;
        TBox = new Closure(projection.Binding.Profile, Terms, Budget);
        TBoxGraph = Terms.Handle(projection.TBoxGraph);
    }

    internal ProjectionTerms Terms { get; }

    internal FactBudget Budget { get; }

    /// <summary>The handle the TBox conclusions' graph's rows are placed under.</summary>
    internal TermHandle TBoxGraph { get; }

    protected InferenceProjection Projection { get; }

    protected Closure TBox { get; }

    /// <summary>The closures of <paramref name="source"/>'s quads, from scratch, over <paramref name="terms"/>, whose source it becomes.</summary>
    /// <exception cref="ClosureLimitExceededException">They would pass the binding's limit; nothing is kept.</exception>
    internal static Materialisation Build(IQuadSource source, ProjectionTerms terms, InferenceProjection projection, CancellationToken cancellationToken)
    {
        terms.Source = source;
        Materialisation built = projection.Binding.CrossGraph ? new CrossGraphMaterialisation(terms, projection) : new PerGraphMaterialisation(terms, projection);
        try
        {
            // Admission (ADR 0195 point 6): each given fact
            // read is charged as a working entry before it is buffered, and the
            // build is refused before it derives anything if the given facts alone
            // could not be held.
            MemoryAmount entry = built.Budget.Costs.PerWorkingEntry;
            long given = 0;
            List<Fact> tbox = [];
            Dictionary<TermHandle, List<Fact>> graphs = [];
            try
            {
                using (IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
                {
                    while (cursor.MoveNext())
                    {
                        Quad quad = cursor.Current;
                        Fact fact = new(quad.Subject, quad.Predicate, quad.Object);
                        built.Warm(quad);
                        if (built.IsTBox(source, quad.Graph))
                        {
                            built.Budget.TakeBeside(entry);
                            given++;
                            tbox.Add(fact);
                        }
                        else if (built.IsData(source, quad.Graph))
                        {
                            built.Budget.TakeBeside(entry);
                            given++;
                            if (!graphs.TryGetValue(quad.Graph, out List<Fact>? facts))
                            {
                                graphs[quad.Graph] = facts = [];
                            }

                            facts.Add(fact);
                        }
                    }
                }

                built.Budget.Admit(new QuadCount(given));
                cancellationToken.ThrowIfCancellationRequested();
                built.TBox.Apply([.. tbox], []);
                built.Load(graphs, cancellationToken);
            }
            finally
            {
                built.Budget.GiveBeside(new MemoryAmount(given * entry.Value));
            }

            projection.Learn(built);
            return built;
        }
        catch
        {
            built.Release();
            throw;
        }
    }

    /// <summary>
    /// Applies a commit's exact delta. Answers, per graph, the facts whose row
    /// may have changed; or null when the commit changes a TBox graph, which
    /// is a generation.
    /// </summary>
    /// <exception cref="ClosureLimitExceededException">A closure would pass the binding's limit.</exception>
    internal Dictionary<TermHandle, HashSet<Fact>>? Apply(IQuadSource view, QuadDelta delta)
    {
        List<Fact> tboxAsserted = [], tboxRetracted = [];
        Dictionary<TermHandle, (List<Fact> Asserted, List<Fact> Retracted)> graphs = [];
        Split(view, delta.Asserted, tboxAsserted, graphs, asserted: true);
        Split(view, delta.Retracted, tboxRetracted, graphs, asserted: false);
        if (tboxAsserted.Count > 0 || tboxRetracted.Count > 0)
        {
            // The TBox closure is shared beneath every data graph's, and never changes under one.
            return null;
        }

        // Every store read before anything changes (ADR 0195 point 6): the commit's terms resolved, and which graphs it
        // empties. A read that fails here leaves the closures as they were,
        // and the commit is applied again when the store answers.
        foreach (Quad quad in delta.Asserted)
        {
            Warm(quad);
        }

        foreach (Quad quad in delta.Retracted)
        {
            Warm(quad);
        }

        HashSet<TermHandle> emptied = [];
        foreach ((TermHandle graph, (List<Fact> _, List<Fact> retracted)) in graphs)
        {
            if (retracted.Count > 0 && !HasQuads(view, graph))
            {
                emptied.Add(graph);
            }
        }

        Dictionary<TermHandle, HashSet<Fact>> changed = [];
        try
        {
            ApplyData(graphs, emptied, changed);
        }
        catch (Exception error) when (InferenceProjection.IsStorageFailure(error))
        {
            // After the closures began to change: a pass that read the store too late, a defect.
            throw new InvalidOperationException("The store failed after the pass had begun to change the closures; every read belongs before it.", error);
        }

        return changed;
    }

    /// <summary>Resolves a quad's terms into those the projection keeps, each new one charged first.</summary>
    private void Warm(in Quad quad)
    {
        WarmOne(quad.Subject);
        WarmOne(quad.Predicate);
        WarmOne(quad.Object);
        WarmOne(quad.Graph);
    }

    private void WarmOne(TermHandle handle)
    {
        if (handle.IsNone || ProjectionTerms.IsLocal(handle))
        {
            return;
        }

        Budget.TakeBeside(InferenceOptions.TermBytes);
        _termCharge += InferenceOptions.TermBytes.Value;
        if (!Terms.Warm(handle))
        {
            // Kept already: nothing new to hold.
            Budget.GiveBeside(InferenceOptions.TermBytes);
            _termCharge -= InferenceOptions.TermBytes.Value;
        }
    }

    /// <summary>The data graphs whose quads were all retracted since the last call.</summary>
    internal List<TermHandle> TakeDropped()
    {
        List<TermHandle> dropped = [.. _dropped];
        _dropped.Clear();
        return dropped;
    }

    /// <summary>The closure that explains a row of a graph: the TBox closure for its conclusions' graph; null for a graph with none.</summary>
    internal abstract Closure? ClosureOf(TermHandle graph);

    /// <summary>Whether the TBox closure holds a fact: a node of a proof it holds is a TBox conclusion.</summary>
    internal bool InTBox(in Fact fact) => TBox.Contains(fact);

    /// <summary>Whether a fact is a row of a graph now.</summary>
    internal bool IsRow(TermHandle graph, in Fact fact) =>
        graph == TBoxGraph ? TBox.Contains(fact) && !TBox.IsExplicit(fact) && Servable(fact) : IsDataRow(graph, fact);

    /// <summary>Every row now.</summary>
    internal IEnumerable<(TermHandle Graph, Fact Fact)> Rows()
    {
        foreach (Fact fact in TBox.Implicit)
        {
            if (Servable(fact))
            {
                yield return (TBoxGraph, fact);
            }
        }

        foreach ((TermHandle Graph, Fact Fact) row in DataRows())
        {
            yield return row;
        }
    }

    /// <summary>The clashes now, each under the graph whose closure finds it; the TBox closure's under its graph.</summary>
    internal IReadOnlyList<PlacedClash> Clashes()
    {
        List<PlacedClash> clashes = [];
        foreach (Clash clash in TBox.Clashes())
        {
            clashes.Add(new PlacedClash(TBoxGraph, clash));
        }

        DataClashes(clashes);
        return clashes;
    }

    /// <summary>Gives every closure's facts back to the budget.</summary>
    internal virtual void Release()
    {
        Budget.GiveBeside(new MemoryAmount(_termCharge));
        _termCharge = 0;
        TBox.Release();
    }

    /// <summary>Every premise-set graph minted, and the data graphs of each (ADR 0190 point 6): none for an intra-graph binding. A graph's name is its set's, so an entry never changes.</summary>
    internal virtual IReadOnlyDictionary<TermHandle, TermHandle[]> PremiseSetGraphs => new Dictionary<TermHandle, TermHandle[]>();

    /// <summary>Closes the data graphs' quads over the TBox closure, from scratch.</summary>
    protected abstract void Load(Dictionary<TermHandle, List<Fact>> graphs, CancellationToken cancellationToken);

    /// <summary>Applies a data commit, graph by graph, recording per graph the facts whose row may have changed.</summary>
    /// <summary>Applies a data commit's delta, graph by graph, recording per graph the facts whose row may have changed; <paramref name="emptied"/> the graphs it left with no quad, read before.</summary>
    protected abstract void ApplyData(Dictionary<TermHandle, (List<Fact> Asserted, List<Fact> Retracted)> graphs, HashSet<TermHandle> emptied, Dictionary<TermHandle, HashSet<Fact>> changed);

    protected abstract bool IsDataRow(TermHandle graph, in Fact fact);

    protected abstract IEnumerable<(TermHandle Graph, Fact Fact)> DataRows();

    protected abstract void DataClashes(List<PlacedClash> into);

    protected void Dropped(TermHandle graph) => _dropped.Add(graph);

    protected bool Servable(in Fact fact) => !Terms.IsLiteral(fact.Subject) && Terms.IsIri(fact.Predicate);

    protected static HashSet<Fact> Into(Dictionary<TermHandle, HashSet<Fact>> changed, TermHandle graph)
    {
        if (!changed.TryGetValue(graph, out HashSet<Fact>? facts))
        {
            changed[graph] = facts = [];
        }

        return facts;
    }

    protected static bool HasQuads(IQuadSource view, TermHandle graph)
    {
        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, graph.IsNone ? GraphPattern.DefaultGraph : GraphPattern.Named(graph));
        return cursor.MoveNext();
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
                foreach (RdfTerm bound in Projection.Binding.TBoxGraphs)
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
                ? Projection.IsData(null)
                : source.TryExternalise(graph, out RdfTerm? term) && Projection.IsData(term);
            _data[graph] = data;
        }

        return data;
    }
}

/// <summary>
/// An intra-graph binding's closures (ADR 0190 point 2): one per data graph,
/// of its own quads over the TBox closure. A fact is a row of a data graph
/// when that graph's closure derives it, it is not explicit there, and the
/// TBox closure does not hold it.
/// </summary>
internal sealed class PerGraphMaterialisation(ProjectionTerms terms, InferenceProjection projection) : Materialisation(terms, projection)
{
    private readonly Dictionary<TermHandle, Closure> _graphs = [];

    internal override Closure? ClosureOf(TermHandle graph) => graph == TBoxGraph ? TBox : _graphs.GetValueOrDefault(graph);

    internal override void Release()
    {
        base.Release();
        foreach (Closure closure in _graphs.Values)
        {
            closure.Release();
        }

        _graphs.Clear();
    }

    protected override void Load(Dictionary<TermHandle, List<Fact>> graphs, CancellationToken cancellationToken)
    {
        foreach ((TermHandle graph, List<Fact> facts) in graphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Closure closure = new(Projection.Binding.Profile, Terms, Budget, TBox);
            _graphs[graph] = closure;
            closure.Apply([.. facts], []);
        }
    }

    protected override void ApplyData(Dictionary<TermHandle, (List<Fact> Asserted, List<Fact> Retracted)> graphs, HashSet<TermHandle> emptied, Dictionary<TermHandle, HashSet<Fact>> changed)
    {
        foreach ((TermHandle graph, (List<Fact> asserted, List<Fact> retracted)) in graphs)
        {
            HashSet<Fact> into = Into(changed, graph);
            if (!_graphs.TryGetValue(graph, out Closure? closure))
            {
                closure = new Closure(Projection.Binding.Profile, Terms, Budget, TBox);
                _graphs[graph] = closure;
            }

            into.UnionWith(closure.Apply([.. asserted], [.. retracted]));
            into.UnionWith(asserted);
            into.UnionWith(retracted);

            if (emptied.Contains(graph))
            {
                closure.Release();
                _graphs.Remove(graph);
                Dropped(graph);
            }
        }
    }

    protected override bool IsDataRow(TermHandle graph, in Fact fact) =>
        _graphs.TryGetValue(graph, out Closure? closure)
        && closure.Contains(fact) && !closure.IsExplicit(fact) && !TBox.Contains(fact) && Servable(fact);

    protected override IEnumerable<(TermHandle Graph, Fact Fact)> DataRows()
    {
        foreach ((TermHandle graph, Closure closure) in _graphs)
        {
            foreach (Fact fact in closure.Own)
            {
                if (!closure.IsExplicit(fact) && !TBox.Contains(fact) && Servable(fact))
                {
                    yield return (graph, fact);
                }
            }
        }
    }

    protected override void DataClashes(List<PlacedClash> into)
    {
        foreach ((TermHandle graph, Closure closure) in _graphs)
        {
            foreach (Clash clash in closure.Clashes())
            {
                bool tboxOnly = true;
                foreach (Fact premise in clash.Premises)
                {
                    tboxOnly &= TBox.Contains(premise);
                }

                if (!tboxOnly)
                {
                    into.Add(new PlacedClash(graph, clash));
                }
            }
        }
    }
}
