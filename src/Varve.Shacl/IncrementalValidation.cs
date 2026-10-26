// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using Varve.Rdf;
using Varve.Shacl.Incremental;
using Varve.Shacl.Model;
using Varve.Shacl.Shapes;
using Varve.Shacl.Validation;

namespace Varve.Shacl;

/// <summary>
/// Validation of a delta (ADR 0171): only the (shape, focus node) pairs the
/// delta affects, before it and after it. Every pair it does not affect keeps
/// its results, so a caller holding the whole report before the delta has
/// the whole report after it, without running a full validation.
/// </summary>
public static class IncrementalValidation
{
    /// <summary>
    /// Validates the pairs <paramref name="delta"/> affects in the data graph
    /// <paramref name="dataGraph"/> of <paramref name="after"/>, the state
    /// after the delta, and in the state before it, which is
    /// <paramref name="after"/> with the delta undone. The delta's quads must
    /// be exact (asserted ones in <paramref name="after"/>, retracted ones
    /// not), and a quad whose graph is outside the data graph is ignored.
    /// </summary>
    public static IncrementalOutcome Compare(
        ShapesGraph shapes,
        IQuadSource after,
        GraphPattern dataGraph,
        QuadDelta delta,
        ShaclOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(after);
        options ??= ShaclOptions.Default;

        QuadDelta changes = InDataGraph(delta, dataGraph);
        IQuadSource before = changes.IsEmpty ? after : new QuadOverlay(after, changes.Inverse());
        ValidationRun pre = new(shapes, new DataGraph(before, dataGraph, null), options, cancellationToken);
        ValidationRun post = new(shapes, new DataGraph(after, dataGraph, null), options, cancellationToken);
        try
        {
            return new IncrementalOutcome(Compare(shapes, pre, post, changes), []);
        }
        catch (ShaclFailureException failure)
        {
            return new IncrementalOutcome(null, [failure.Problem]);
        }
    }

    private static IncrementalComparison Compare(ShapesGraph shapes, ValidationRun pre, ValidationRun post, QuadDelta changes)
    {
        AffectedPairs affected = new(shapes, pre, post);
        foreach (Quad quad in changes.Asserted)
        {
            Add(affected, post.Data, quad);
        }

        foreach (Quad quad in changes.Retracted)
        {
            Add(affected, post.Data, quad);
        }

        List<ResultRecord> before = [], after = [];
        ResultSink beforeSink = ResultSink.Collecting(before);
        HashSet<Node> focus = new(pre.Data.Nodes);
        List<FocusPair> focusPairs = [];
        List<ResultRecord> pairRecords = [];
        ResultSink pairSink = ResultSink.Collecting(pairRecords);
        int pairs = 0;
        foreach ((Shape shape, Node node) in affected.Pairs())
        {
            if (pre.IsTarget(shape, node))
            {
                pre.ValidateFocus(shape, node, beforeSink);
                focus.Add(node);
                pairs++;
            }

            if (post.IsTarget(shape, node))
            {
                pairRecords.Clear();
                post.ValidateFocus(shape, node, pairSink);
                after.AddRange(pairRecords);
                focusPairs.Add(new FocusPair(shape.Node, post.Data.Term(node), ValidationReport.Create(post, pairRecords)));
                focus.Add(node);
                pairs++;
            }
            else
            {
                focusPairs.Add(new FocusPair(shape.Node, post.Data.Term(node), null));
            }
        }

        List<RdfTerm> focusTerms = [];
        foreach (Node node in focus)
        {
            focusTerms.Add(post.Data.Term(node));
        }

        focusTerms.Sort(static (a, b) => Varve.Xsd.XsdString.CompareCodePoints(Strings.NTriples(a), Strings.NTriples(b)));

        ValidationReport afterReport = ValidationReport.Create(post, after);
        return new IncrementalComparison(
            focusTerms,
            ValidationReport.Create(pre, before),
            afterReport,
            Introduced(shapes, pre, post, before, after, afterReport),
            pairs,
            focusPairs);
    }

    private static void Add(AffectedPairs affected, DataGraph data, in Quad quad)
    {
        Node predicate = Node.Held(quad.Predicate);
        affected.Add(Node.Held(quad.Subject), predicate, data.Term(predicate), Node.Held(quad.Object));
    }

    /// <summary>The delta without the quads outside the data graph.</summary>
    private static QuadDelta InDataGraph(QuadDelta delta, GraphPattern dataGraph)
    {
        if (dataGraph.Match == GraphMatch.Any)
        {
            return delta;
        }

        List<Quad> asserted = [], retracted = [];
        foreach (Quad quad in delta.Asserted)
        {
            if (dataGraph.Matches(quad.Graph))
            {
                asserted.Add(quad);
            }
        }

        foreach (Quad quad in delta.Retracted)
        {
            if (dataGraph.Matches(quad.Graph))
            {
                retracted.Add(quad);
            }
        }

        return QuadDelta.Create([.. asserted], [.. retracted]);
    }

    /// <summary>
    /// The results after whose identity is in no result of the whole report
    /// before (ADR 0169's R1). An identity missing from the affected pairs
    /// before may still come from a pair the delta did not affect, through
    /// <c>sh:property</c>: those pairs are found up the property chains from
    /// the result's source shape and focus node, and validated before.
    /// </summary>
    private static List<ShaclResult> Introduced(ShapesGraph shapes, ValidationRun pre, ValidationRun post, List<ResultRecord> before, List<ResultRecord> after, ValidationReport afterReport)
    {
        HashSet<ResultIdentity> known = [];
        foreach (ResultRecord record in before)
        {
            known.Add(ResultIdentity.Of(pre.Data, record));
        }

        List<ShaclResult> introduced = [];
        Dictionary<ResultIdentity, bool> decided = [];
        for (int i = 0; i < afterReport.Results.Count; i++)
        {
            ResultRecord record = after[afterReport.Sources[i]];
            ResultIdentity identity = ResultIdentity.Of(post.Data, record);
            if (known.Contains(identity))
            {
                continue;
            }

            if (!decided.TryGetValue(identity, out bool found))
            {
                found = Provenance(shapes, pre, record, identity, known);
                decided[identity] = found;
            }

            if (!found)
            {
                introduced.Add(afterReport.Results[i]);
            }
        }

        return introduced;
    }

    /// <summary>Whether a pair before the delta, up the property chains from the record's shape and focus node, produces its identity.</summary>
    private static bool Provenance(ShapesGraph shapes, ValidationRun pre, ResultRecord record, ResultIdentity wanted, HashSet<ResultIdentity> known)
    {
        ShapesFootprint footprint = shapes.Footprint;
        Queue<(Shape Shape, Node Node)> pending = new();
        HashSet<(int, Node)> visited = new(new ShapeNodeComparer(pre.Data.Nodes));
        pending.Enqueue((record.Shape, record.Focus));
        visited.Add((record.Shape.Index, record.Focus));
        List<ResultRecord> results = [];
        while (pending.Count > 0)
        {
            (Shape shape, Node node) = pending.Dequeue();
            if (!shape.Deactivated && (shape.HasTargets || shape.Node.Kind == RdfTermKind.Iri))
            {
                IEnumerable<Node> foci = shape.Values is not null || shape.DefaultValue is not null ? pre.Targets(shape) : [node];
                foreach (Node focus in foci)
                {
                    if (!pre.IsTarget(shape, focus))
                    {
                        continue;
                    }

                    results.Clear();
                    pre.ValidateFocus(shape, focus, ResultSink.Collecting(results));
                    foreach (ResultRecord found in results)
                    {
                        ResultIdentity identity = ResultIdentity.Of(pre.Data, found);
                        known.Add(identity);
                        if (identity.Equals(wanted))
                        {
                            return true;
                        }
                    }
                }
            }

            foreach (int parent in footprint.PropertyParents[shape.Index])
            {
                Shape up = shapes.Shapes[parent];
                List<Node> sources = [];
                if (up.Path is { } path)
                {
                    pre.PathSources(path, node, sources, new HashSet<Node>(pre.Data.Nodes));
                }
                else
                {
                    sources.Add(node);
                }

                foreach (Node source in sources)
                {
                    if (visited.Add((parent, source)))
                    {
                        pending.Enqueue((up, source));
                    }
                }
            }
        }

        return false;
    }

    private sealed class ShapeNodeComparer(NodeComparer nodes) : IEqualityComparer<(int, Node)>
    {
        public bool Equals((int, Node) x, (int, Node) y) => x.Item1 == y.Item1 && nodes.Equals(x.Item2, y.Item2);

        public int GetHashCode((int, Node) obj) => HashCode.Combine(obj.Item1, nodes.GetHashCode(obj.Item2));
    }
}

/// <summary>An incremental validation's outcome: the comparison, or the problems of a failure (ADR 0151).</summary>
public sealed class IncrementalOutcome
{
    internal IncrementalOutcome(IncrementalComparison? comparison, IReadOnlyList<ShaclProblem> problems)
    {
        Comparison = comparison;
        Problems = problems;
    }

    /// <summary>The comparison, or null when the validation failed.</summary>
    public IncrementalComparison? Comparison { get; }

    /// <summary>Why it failed; empty when there is a comparison.</summary>
    public IReadOnlyList<ShaclProblem> Problems { get; }

    /// <summary>Whether there is a comparison.</summary>
    public bool Succeeded => Comparison is not null;
}
