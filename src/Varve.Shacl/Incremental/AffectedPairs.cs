// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Derivation;
using Varve.Derivation.Model;
using Varve.Rdf;
using Varve.Shacl.Shapes;
using Varve.Shacl.Validation;

namespace Varve.Shacl.Incremental;

/// <summary>
/// The (shape, focus node) pairs a delta affects (ADR 0171): each changed
/// triple's anchors per shape, propagated along the dependency graph to the
/// least fixpoint by <c>Varve.Derivation</c>, with the nodes the delta may
/// move into or out of each shape's targets. Every search runs in the state
/// before the delta and the state after it, which over-approximates and so is
/// sound.
/// </summary>
internal sealed class AffectedPairs
{
    private static readonly PathAutomaton<RdfTerm> ListHeads =
        RegularPath.ZeroOrMore(RegularPath.Edge(Vocabulary.RdfRest)).Compile();

    private static readonly PathAutomaton<RdfTerm> ListMembers =
        RegularPath.Sequence([RegularPath.ZeroOrMore(RegularPath.Edge(Vocabulary.RdfRest)), RegularPath.Edge(Vocabulary.RdfFirst)]).Compile();

    private readonly ShapesGraph _shapes;
    private readonly ShapesFootprint _footprint;
    private readonly ValidationRun[] _runs;
    private readonly Propagation<Node, RdfTerm> _affected;
    private readonly HashSet<Node>[] _targets;
    private readonly bool[] _full;
    private readonly List<int> _readers = [];
    private bool _any;

    internal AffectedPairs(ShapesGraph shapes, ValidationRun before, ValidationRun after)
    {
        _shapes = shapes;
        _footprint = shapes.Footprint;
        _runs = [before, after];
        _affected = new Propagation<Node, RdfTerm>(_footprint.Graph, _footprint.Strata, before.Data.Nodes);
        _targets = new HashSet<Node>[shapes.Shapes.Length];
        for (int s = 0; s < _targets.Length; s++)
        {
            _targets[s] = new HashSet<Node>(before.Data.Nodes);
        }

        _full = new bool[shapes.Shapes.Length];
    }

    /// <summary>Adds one changed triple of the data graph, asserted or retracted, its predicate's term given.</summary>
    [DesignDecision(typeof(WhichShapesAreIncrementallyMaintainable.AnchorsInBothStates), Scope = ExceptionScope.HotPath)]
    internal void Add(Node subject, Node predicate, RdfTerm predicateTerm, Node obj)
    {
        _any = true;
        _readers.Clear();
        _footprint.Readers(predicateTerm, _readers);
        foreach (int s in _readers)
        {
            Shape shape = _shapes.Shapes[s];
            foreach (ValidationRun run in _runs)
            {
                Local(shape, run, subject, predicate, predicateTerm, obj);
            }
        }
    }

    /// <summary>
    /// Closes the affected sets under the dependency graph and yields each
    /// pair to validate: a shape that can have targets, and a node that may
    /// be its target before or after the delta.
    /// </summary>
    internal IEnumerable<(Shape Shape, Node Node)> Pairs()
    {
        System.Collections.Immutable.ImmutableArray<Shape> shapes = _shapes.Shapes;
        for (int s = 0; s < _full.Length; s++)
        {
            _full[s] = _any && (_footprint.Constants[s] || _footprint.Opaque[s]);
        }

        // A dependant of a full shape is full: its value nodes' conformance may change anywhere.
        bool grew = true;
        while (grew)
        {
            grew = false;
            for (int s = 0; s < _full.Length; s++)
            {
                if (_full[s])
                {
                    continue;
                }

                foreach (DependencyEdge<RdfTerm> edge in _footprint.Graph.Outgoing(s))
                {
                    if (_full[edge.Target])
                    {
                        _full[s] = grew = true;
                        break;
                    }
                }
            }
        }

        // §7.9.5: a node entering or leaving the targets changes the count of its key.
        for (int s = 0; s < _full.Length; s++)
        {
            foreach (Constraint constraint in shapes[s].Constraints)
            {
                if (constraint is UniqueValuesForConstraint unique)
                {
                    foreach (ValidationRun run in _runs)
                    {
                        List<Node> sharing = [];
                        foreach (Node target in _targets[s])
                        {
                            Sharers(run, unique, target, sharing);
                        }

                        Seed(shapes[s], run, sharing);
                    }
                }
            }
        }

        _affected.Run(Transfer);

        HashSet<Node> seen = new(_runs[0].Data.Nodes);
        for (int s = 0; s < _full.Length; s++)
        {
            Shape shape = shapes[s];
            if (shape.Deactivated || (!shape.HasTargets && shape.Node.Kind != RdfTermKind.Iri))
            {
                continue;
            }

            seen.Clear();
            if (_full[s])
            {
                foreach (ValidationRun run in _runs)
                {
                    foreach (Node node in run.Targets(shape))
                    {
                        if (seen.Add(node))
                        {
                            yield return (shape, node);
                        }
                    }
                }

                continue;
            }

            foreach (Node node in _affected.SetOf(s))
            {
                if (seen.Add(node))
                {
                    yield return (shape, node);
                }
            }

            foreach (Node node in _targets[s])
            {
                if (seen.Add(node))
                {
                    yield return (shape, node);
                }
            }
        }
    }

    /// <summary>The anchors of one changed triple for one shape, in one state (ADR 0171's table).</summary>
    private void Local(Shape shape, ValidationRun run, Node x, Node p, RdfTerm predicate, Node y)
    {
        int s = shape.Index;
        DataGraph data = run.Data;
        bool isType = predicate.Equals(Vocabulary.RdfType);
        bool isSubClassOf = predicate.Equals(Vocabulary.RdfsSubClassOf);

        // Targets (§3.1.3): nodes the triple may move into or out of them.
        if (shape.TargetClasses.Count > 0)
        {
            if (isType)
            {
                _targets[s].Add(x);
            }
            else if (isSubClassOf)
            {
                _targets[s].UnionWith(run.Instances(x));
            }
        }

        if (shape.TargetSubjectsOf.Contains(predicate))
        {
            _targets[s].Add(x);
        }

        if (shape.TargetObjectsOf.Contains(predicate))
        {
            _targets[s].Add(y);
        }

        if (shape.TargetWhere.Count > 0)
        {
            _targets[s].Add(x);
            _targets[s].Add(y);
        }

        if (predicate.Equals(Vocabulary.Shape) && data.Term(y).Equals(shape.Node))
        {
            _targets[s].Add(x);
        }

        // The path's triples (§4): the focus nodes that reach the triple by the path's prefix.
        List<Node> focus = [];
        HashSet<Node> seen = new(data.Nodes);
        shape.Path?.Automaton.Anchors(data, x, predicate, y, focus, seen);

        // What the components read at value nodes, or at the focus node directly (§7).
        List<Node> values = [];
        foreach (Constraint constraint in shape.Constraints)
        {
            switch (constraint)
            {
                case ClassConstraint:
                    if (isType)
                    {
                        values.Add(x);
                    }
                    else if (isSubClassOf)
                    {
                        values.AddRange(run.Instances(x));
                    }

                    break;

                case ClosedConstraint:
                    values.Add(x);
                    if (isSubClassOf)
                    {
                        values.AddRange(run.Instances(x));
                    }

                    break;

                case ListConstraint:
                    if (predicate.Equals(Vocabulary.RdfFirst) || predicate.Equals(Vocabulary.RdfRest))
                    {
                        ListHeads.Reverse(data, x, values, new HashSet<Node>(data.Nodes));
                    }

                    break;

                case RootClassConstraint:
                    if (isSubClassOf)
                    {
                        values.AddRange(run.SubclassesOf(x));
                    }

                    break;

                case PairConstraint pair:
                    pair.Other.Automaton.Anchors(data, x, predicate, y, focus, seen);
                    break;

                case ReifierShapeConstraint reifier:
                    if (predicate.Equals(Vocabulary.RdfReifies))
                    {
                        Reified(run, reifier, y, focus, seen);
                    }

                    break;

                case UniqueValuesForConstraint unique:
                    if (unique.Properties.Contains(predicate))
                    {
                        Sharers(run, unique, x, values);
                    }

                    break;

                case ExtensionConstraint { Inner: IShaclFootprint footprint }:
                    if (x.IsHeld && p.IsHeld && y.IsHeld)
                    {
                        List<TermHandle> anchors = [];
                        footprint.Anchors(new FootprintContext(data.Source, data.Graph, x.Handle, p.Handle, y.Handle, anchors));
                        foreach (TermHandle anchor in anchors)
                        {
                            if (seen.Add(Node.Held(anchor)))
                            {
                                focus.Add(Node.Held(anchor));
                            }
                        }
                    }

                    break;
            }
        }

        foreach (Node node in focus)
        {
            _affected.Seed(s, node);
        }

        Seed(shape, run, values);
    }

    /// <summary>Seeds a shape with the focus nodes whose value nodes include one of <paramref name="values"/>.</summary>
    private void Seed(Shape shape, ValidationRun run, List<Node> values)
    {
        List<Node> focus = [];
        Sources(shape, run, values, focus);
        foreach (Node node in focus)
        {
            _affected.Seed(shape.Index, node);
        }
    }

    /// <summary>Rev_s: the nodes from which the shape's value nodes include one of <paramref name="values"/> (§6.9).</summary>
    private static void Sources(Shape shape, ValidationRun run, List<Node> values, List<Node> into)
    {
        if (shape.Path is not { } path)
        {
            into.AddRange(values);
            return;
        }

        HashSet<Node> seen = new(run.Data.Nodes);
        foreach (Node value in values)
        {
            run.PathSources(path, value, into, seen);
        }
    }

    /// <summary>
    /// Moves a node whose conformance to the edge's target may have changed
    /// to the source shape's focus nodes that may have changed with it.
    /// </summary>
    private void Transfer(DependencyEdge<RdfTerm> edge, Node node, List<Node> into)
    {
        Shape shape = _shapes.Shapes[edge.Source];
        foreach (ValidationRun run in _runs)
        {
            if (edge.Label.Equals(Vocabulary.TargetWhere))
            {
                into.Add(node);
            }
            else if (edge.Label.Equals(Vocabulary.MemberShape))
            {
                List<Node> heads = [];
                ListMembers.Reverse(run.Data, node, heads, new HashSet<Node>(run.Data.Nodes));
                Sources(shape, run, heads, into);
            }
            else if (edge.Label.Equals(Vocabulary.ReifierShape))
            {
                foreach (Constraint constraint in shape.Constraints)
                {
                    if (constraint is ReifierShapeConstraint reifier && reifier.ReifierShape == edge.Target)
                    {
                        Reifications(run, reifier, node, into);
                    }
                }
            }
            else
            {
                Sources(shape, run, [node], into);
            }
        }
    }

    /// <summary>§7.8.5: the subject of the reified triple <paramref name="tripleTerm"/>, when its predicate is the constraint's.</summary>
    private static void Reified(ValidationRun run, ReifierShapeConstraint reifier, Node tripleTerm, List<Node> into, HashSet<Node> seen)
    {
        DataGraph data = run.Data;
        if (tripleTerm.IsHeld && data.Source.TryGetTripleTermComponents(tripleTerm.Handle, out TermHandle s, out TermHandle p, out TermHandle _)
            && data.Term(Node.Held(p)).Equals(reifier.Predicate) && seen.Add(Node.Held(s)))
        {
            into.Add(Node.Held(s));
        }
    }

    /// <summary>The subjects of the triples a reifier reifies, when their predicate is the constraint's.</summary>
    private static void Reifications(ValidationRun run, ReifierShapeConstraint reifier, Node node, List<Node> into)
    {
        DataGraph data = run.Data;
        if (!node.IsHeld || !data.TryHandle(Vocabulary.RdfReifies, out TermHandle reifies))
        {
            return;
        }

        List<Node> terms = [];
        data.Objects(node, reifies, terms, new HashSet<Node>(data.Nodes));
        HashSet<Node> seen = new(data.Nodes);
        foreach (Node term in terms)
        {
            Reified(run, reifier, term, into, seen);
        }
    }

    /// <summary>§7.9.5: the node, and every node with a value in common with it for one of the properties — erring wide over equal keys.</summary>
    private static void Sharers(ValidationRun run, UniqueValuesForConstraint unique, Node node, List<Node> into)
    {
        DataGraph data = run.Data;
        into.Add(node);
        foreach (RdfTerm property in unique.Properties)
        {
            if (!data.TryHandle(property, out TermHandle p))
            {
                continue;
            }

            List<Node> values = [];
            data.Objects(node, p, values, new HashSet<Node>(data.Nodes));
            HashSet<Node> seen = new(data.Nodes);
            foreach (Node value in values)
            {
                data.Subjects(p, value, into, seen);
            }
        }
    }
}
