// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Shapes;
using Varve.Xsd;

namespace Varve.Shacl.Validation;

/// <summary>
/// One validation of one data graph against one shapes graph (SHACL 1.2 Core
/// §6.6): its memo tables, its compiled patterns, and conformance to a shape
/// answered by ADR 0150's stratified greatest fixpoint. Used by one thread,
/// for one call, and dropped after it.
/// </summary>
internal sealed class ValidationRun
{
    private readonly HashSet<RdfTerm> _disallowed;
    private readonly Dictionary<(int Shape, Node Node), bool> _conforms;
    private readonly Dictionary<int, Dictionary<(int Shape, Node Node), bool>> _fixpoints = [];
    private readonly Dictionary<Node, HashSet<Node>> _superclasses;
    private readonly Dictionary<Node, List<Node>> _types;
    private readonly Dictionary<int, List<Node>> _targets = [];
    private readonly Dictionary<string, Regex> _patterns = new(StringComparer.Ordinal);
    private readonly PathEvaluator _paths;
    private Dictionary<(Node, Node, Node), List<Node>>? _reifiers;
    private int _steps;

    internal ValidationRun(ShapesGraph shapes, DataGraph data, ShaclOptions options, CancellationToken cancellationToken)
    {
        Shapes = shapes;
        Data = data;
        Options = options;
        CancellationToken = cancellationToken;
        _disallowed = new HashSet<RdfTerm>(options.ConformanceDisallows ?? ShaclOptions.DefaultConformanceDisallows, RdfTerm.Comparer);
        _conforms = new Dictionary<(int, Node), bool>(new PairComparer(data.Nodes));
        _superclasses = new Dictionary<Node, HashSet<Node>>(data.Nodes);
        _types = new Dictionary<Node, List<Node>>(data.Nodes);
        _paths = new PathEvaluator(data);
    }

    internal DataGraph Data { get; }

    internal ShaclOptions Options { get; }

    internal CancellationToken CancellationToken { get; }

    internal ShapesGraph Shapes { get; }

    internal Shape Shape(int index) => Shapes.Shapes[index];

    /// <summary>The validation of the data graph against every shape (§6.6), into <paramref name="results"/>.</summary>
    internal void ValidateAll(List<ResultRecord> results)
    {
        ResultSink sink = ResultSink.Collecting(results);
        foreach (Shape shape in Shapes.Shapes)
        {
            if (shape.Deactivated)
            {
                continue;
            }

            foreach (Node focus in Targets(shape))
            {
                ValidateFocus(shape, focus, sink);
            }
        }
    }

    /// <summary>The validation of a focus node against a shape (§6.6): every active constraint, over the shape's value nodes.</summary>
    internal void ValidateFocus(Shape shape, Node focus, ResultSink sink)
    {
        if (shape.Deactivated)
        {
            return;
        }

        if ((++_steps & 0xFF) == 0)
        {
            CancellationToken.ThrowIfCancellationRequested();
        }

        IReadOnlyList<Node> values = ValueNodes(shape, focus);
        foreach (Constraint constraint in shape.Constraints)
        {
            if (!constraint.Deactivated)
            {
                constraint.Validate(this, shape, focus, values, sink);
            }
        }
    }

    /// <summary>§6.9: the focus node for a node shape; for a property shape, its path's values, then <c>sh:values</c>, then <c>sh:defaultValue</c>.</summary>
    internal IReadOnlyList<Node> ValueNodes(Shape shape, Node focus)
    {
        if (shape.Path is null)
        {
            return [focus];
        }

        List<Node> values = _paths.Values(shape.Path, focus);
        if (shape.Values is { } constant)
        {
            Node node = Data.Resolve(constant);
            if (!values.Exists(v => Data.Nodes.Equals(v, node)))
            {
                values.Add(node);
            }
        }

        if (values.Count == 0 && shape.DefaultValue is { } fallback)
        {
            values.Add(Data.Resolve(fallback));
        }

        return values;
    }

    /// <summary>The nodes <paramref name="path"/> reaches from <paramref name="start"/>.</summary>
    internal List<Node> PathValues(ShapePath path, Node start) => _paths.Values(path, start);

    // ------------------------------------------------------------- targets

    /// <summary>§3.1.3: the target of a shape, every kind of target declaration united, distinct.</summary>
    internal List<Node> Targets(Shape shape)
    {
        if (_targets.TryGetValue(shape.Index, out List<Node>? cached))
        {
            return cached;
        }

        List<Node> targets = [];
        HashSet<Node> seen = new(Data.Nodes);
        void Add(Node node)
        {
            if (seen.Add(node))
            {
                targets.Add(node);
            }
        }

        foreach (RdfTerm node in shape.TargetNodes)
        {
            Add(Data.Resolve(node));
        }

        foreach (RdfTerm cls in shape.TargetClasses)
        {
            foreach (Node instance in Instances(Data.Resolve(cls)))
            {
                Add(instance);
            }
        }

        foreach (RdfTerm predicate in shape.TargetSubjectsOf)
        {
            if (Data.TryHandle(predicate, out TermHandle p))
            {
                using IQuadCursor cursor = Data.Source.Match(TermHandle.None, p, TermHandle.None, Data.Graph);
                while (cursor.MoveNext())
                {
                    Add(Node.Held(cursor.Current.Subject));
                }
            }
        }

        foreach (RdfTerm predicate in shape.TargetObjectsOf)
        {
            if (Data.TryHandle(predicate, out TermHandle p))
            {
                using IQuadCursor cursor = Data.Source.Match(TermHandle.None, p, TermHandle.None, Data.Graph);
                while (cursor.MoveNext())
                {
                    Add(Node.Held(cursor.Current.Object));
                }
            }
        }

        if (shape.TargetWhere.Count > 0)
        {
            // §3.1.3.6: every node of the data graph conforming to the shape; the
            // shape is in a lower stratum (ADR 0150), so this is a plain check.
            List<Node> nodes = Data.AllNodes();
            foreach (int where in shape.TargetWhere)
            {
                foreach (Node node in nodes)
                {
                    if (Conforms(where, node))
                    {
                        Add(node);
                    }
                }
            }
        }

        // §3.1.3.7: n sh:shape s in the data graph.
        if (shape.Node.Kind == RdfTermKind.Iri && Data.TryHandle(Vocabulary.Shape, out TermHandle shapePredicate)
            && Data.TryHandle(shape.Node, out TermHandle shapeNode))
        {
            using IQuadCursor cursor = Data.Source.Match(TermHandle.None, shapePredicate, shapeNode, Data.Graph);
            while (cursor.MoveNext())
            {
                Add(Node.Held(cursor.Current.Subject));
            }
        }

        _targets[shape.Index] = targets;
        return targets;
    }

    // ------------------------------------------------------- SHACL instances

    /// <summary>The SHACL instances of a class (§1.1): <c>rdf:type/rdfs:subClassOf*</c>, reversed.</summary>
    internal List<Node> Instances(Node cls)
    {
        List<Node> instances = [];
        HashSet<Node> seen = new(Data.Nodes);
        if (!Data.TryHandle(Vocabulary.RdfType, out TermHandle type))
        {
            return instances;
        }

        foreach (Node subclass in SubclassesOf(cls))
        {
            Data.Subjects(type, subclass, instances, seen);
        }

        return instances;
    }

    /// <summary>Whether <paramref name="node"/> is a SHACL instance of <paramref name="cls"/>.</summary>
    internal bool IsInstanceOf(Node node, Node cls)
    {
        foreach (Node type in TypesOf(node))
        {
            if (SuperclassesOf(type).Contains(cls))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The <c>rdf:type</c> values of a node in the data graph.</summary>
    internal List<Node> TypesOf(Node node)
    {
        if (_types.TryGetValue(node, out List<Node>? types))
        {
            return types;
        }

        types = [];
        if (Data.TryHandle(Vocabulary.RdfType, out TermHandle type))
        {
            Data.Objects(node, type, types, new HashSet<Node>(Data.Nodes));
        }

        _types[node] = types;
        return types;
    }

    /// <summary>A class and its superclasses, <c>rdfs:subClassOf*</c>, from the data graph and, by option, the shapes graph (§6.4).</summary>
    internal HashSet<Node> SuperclassesOf(Node cls)
    {
        if (_superclasses.TryGetValue(cls, out HashSet<Node>? found))
        {
            return found;
        }

        found = Closure(cls, upward: true);
        _superclasses[cls] = found;
        return found;
    }

    private List<Node> SubclassesOf(Node cls) => [.. Closure(cls, upward: false)];

    private HashSet<Node> Closure(Node start, bool upward)
    {
        HashSet<Node> found = new(Data.Nodes) { start };
        Queue<Node> pending = new();
        pending.Enqueue(start);
        bool hasData = Data.TryHandle(Vocabulary.RdfsSubClassOf, out TermHandle subClassOf);
        while (pending.Count > 0)
        {
            Node current = pending.Dequeue();
            List<Node> next = [];
            if (hasData)
            {
                if (upward)
                {
                    Data.Objects(current, subClassOf, next, new HashSet<Node>(Data.Nodes));
                }
                else
                {
                    Data.Subjects(subClassOf, current, next, new HashSet<Node>(Data.Nodes));
                }
            }

            if (Options.SubClassOfInShapesGraph)
            {
                RdfTerm term = Data.Term(current);
                List<RdfTerm> related = upward
                    ? Shapes.Source.Objects(term, Vocabulary.RdfsSubClassOf)
                    : Shapes.Source.Subjects(Vocabulary.RdfsSubClassOf, term);
                foreach (RdfTerm other in related)
                {
                    next.Add(Data.Resolve(other));
                }
            }

            foreach (Node node in next)
            {
                if (found.Add(node))
                {
                    pending.Enqueue(node);
                }
            }
        }

        return found;
    }

    // --------------------------------------------------------- conformance

    /// <summary>
    /// Whether <paramref name="node"/> conforms to the shape (§6.7): its
    /// validation produces no result of a disallowed severity and no failure.
    /// Inside a cyclic component, the greatest fixpoint of ADR 0150, computed
    /// over the pairs reachable from this one.
    /// </summary>
    internal bool Conforms(int shapeIndex, Node node)
    {
        if (_conforms.TryGetValue((shapeIndex, node), out bool known))
        {
            return known;
        }

        Shape shape = Shape(shapeIndex);
        if (!shape.Cyclic)
        {
            bool conforms = Evaluate(shape, node);
            _conforms[(shapeIndex, node)] = conforms;
            return conforms;
        }

        if (_fixpoints.TryGetValue(shape.Component, out Dictionary<(int, Node), bool>? assignment))
        {
            // Inside this component's iteration: the current assumption, true for a new pair.
            if (!assignment.TryGetValue((shapeIndex, node), out bool assumed))
            {
                assignment[(shapeIndex, node)] = true;
                assumed = true;
            }

            return assumed;
        }

        assignment = new Dictionary<(int, Node), bool>(new PairComparer(Data.Nodes)) { [(shapeIndex, node)] = true };
        _fixpoints[shape.Component] = assignment;
        try
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                int before = assignment.Count;
                foreach ((int Shape, Node Node) pair in new List<(int, Node)>(assignment.Keys))
                {
                    if (assignment[pair] && !Evaluate(Shape(pair.Shape), pair.Node))
                    {
                        assignment[pair] = false;
                        changed = true;
                    }
                }

                changed |= assignment.Count != before;
            }
        }
        finally
        {
            _fixpoints.Remove(shape.Component);
        }

        foreach (KeyValuePair<(int, Node), bool> entry in assignment)
        {
            _conforms[entry.Key] = entry.Value;
        }

        return assignment[(shapeIndex, node)];
    }

    /// <summary>One round: whether validating the node against the shape gives no disallowed result, every nested check answered by the memo or the current assignment.</summary>
    private bool Evaluate(Shape shape, Node node)
    {
        ResultSink check = ResultSink.Checking(_disallowed);
        ValidateFocus(shape, node, check);
        return !check.Disallowed;
    }

    // ---------------------------------------------------------- reifiers

    /// <summary>The reifiers of the triple (§7.8.5): nodes r with <c>r rdf:reifies &lt;&lt;( s p o )&gt;&gt;</c> in the data graph.</summary>
    internal List<Node> Reifiers(Node subject, Node predicate, Node obj)
    {
        if (_reifiers is null)
        {
            _reifiers = new Dictionary<(Node, Node, Node), List<Node>>(new TripleComparer(Data.Nodes));
            if (Data.TryHandle(Vocabulary.RdfReifies, out TermHandle reifies))
            {
                using IQuadCursor cursor = Data.Source.Match(TermHandle.None, reifies, TermHandle.None, Data.Graph);
                while (cursor.MoveNext())
                {
                    Quad quad = cursor.Current;
                    if (Data.Source.TryGetTripleTermComponents(quad.Object, out TermHandle s, out TermHandle p, out TermHandle o))
                    {
                        (Node, Node, Node) key = (Node.Held(s), Node.Held(p), Node.Held(o));
                        if (!_reifiers.TryGetValue(key, out List<Node>? list))
                        {
                            list = [];
                            _reifiers[key] = list;
                        }

                        Node reifier = Node.Held(quad.Subject);
                        if (!list.Exists(r => Data.Nodes.Equals(r, reifier)))
                        {
                            list.Add(reifier);
                        }
                    }
                }
            }
        }

        return _reifiers.TryGetValue((subject, predicate, obj), out List<Node>? found) ? found : [];
    }

    // ----------------------------------------------------------- patterns

    /// <summary>A pattern compiled once per validation, under <c>ShaclOptions.RegexTimeout</c>; a pattern .NET refuses is a failure.</summary>
    internal Regex Pattern(Shape shape, XPathRegexTranslation translation)
    {
        if (_patterns.TryGetValue(translation.Pattern, out Regex? regex))
        {
            return regex;
        }

        if (!XPathRegex.TryCompile(translation, Options.RegexTimeout, out regex, out XPathRegexError error))
        {
            throw new ShaclFailureException(new ShaclProblem(ShaclProblemKind.InvalidPattern, shape.Node, null, "sh:pattern (SHACL 1.2 Core §7.4.3): " + error));
        }

        _patterns[translation.Pattern] = regex;
        return regex;
    }

    private sealed class PairComparer(NodeComparer nodes) : IEqualityComparer<(int Shape, Node Node)>
    {
        public bool Equals((int Shape, Node Node) x, (int Shape, Node Node) y) => x.Shape == y.Shape && nodes.Equals(x.Node, y.Node);

        public int GetHashCode((int Shape, Node Node) obj) => HashCode.Combine(obj.Shape, nodes.GetHashCode(obj.Node));
    }

    private sealed class TripleComparer(NodeComparer nodes) : IEqualityComparer<(Node, Node, Node)>
    {
        public bool Equals((Node, Node, Node) x, (Node, Node, Node) y) =>
            nodes.Equals(x.Item1, y.Item1) && nodes.Equals(x.Item2, y.Item2) && nodes.Equals(x.Item3, y.Item3);

        public int GetHashCode((Node, Node, Node) obj) =>
            HashCode.Combine(nodes.GetHashCode(obj.Item1), nodes.GetHashCode(obj.Item2), nodes.GetHashCode(obj.Item3));
    }
}

/// <summary>A failure (§6.6.1) found during validation: it ends the validation with its problem, never with a report (ADR 0151).</summary>
internal sealed class ShaclFailureException(ShaclProblem problem) : Exception(problem.Message)
{
    internal ShaclProblem Problem { get; } = problem;
}
