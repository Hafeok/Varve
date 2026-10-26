// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Collections.Immutable;
using Varve.Derivation;
using Varve.Derivation.Model;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Shapes;
using Varve.Shacl.Validation;

namespace Varve.Shacl.Incremental;

/// <summary>
/// What a shapes graph reads of the data, worked out once from the shapes
/// alone (ADR 0171): the dependency graph and its strata, each shape's class
/// and the constructs that decide it, which predicates each shape reads, and
/// which shapes pass results up through <c>sh:property</c>.
/// </summary>
internal sealed class ShapesFootprint
{
    private readonly Dictionary<RdfTerm, List<int>> _byPredicate = new(RdfTerm.Comparer);
    private readonly List<int> _anyPredicate = [];
    private readonly Dictionary<RdfTerm, List<int>> _byTargetClass = new(RdfTerm.Comparer);

    private ShapesFootprint(ShapesGraph shapes)
    {
        ImmutableArray<Shape> all = shapes.Shapes;
        Graph = Stratification.Graph(all);
        Strata = Graph.Stratify();
        Constants = new bool[all.Length];
        Opaque = new bool[all.Length];
        PropertyParents = new List<int>[all.Length];
        for (int s = 0; s < all.Length; s++)
        {
            PropertyParents[s] = [];
        }

        List<ClassificationReason>[] own = new List<ClassificationReason>[all.Length];
        foreach (Shape shape in all)
        {
            own[shape.Index] = Read(shape);
        }

        Classification = Classify(all, own);
    }

    /// <summary>The shapes' dependency graph: one edge per reference, labelled by its parameter (ADR 0150).</summary>
    internal DependencyGraph<RdfTerm> Graph { get; }

    internal Stratification<RdfTerm> Strata { get; }

    internal ShapesClassification Classification { get; }

    /// <summary>Whether a shape's value nodes include a constant that a component reads the data at: a change anywhere may affect every focus node.</summary>
    internal bool[] Constants { get; }

    /// <summary>Whether a shape has an extension constraint that does not state its footprint.</summary>
    internal bool[] Opaque { get; }

    /// <summary>For each shape, the shapes that validate it at their value nodes through <c>sh:property</c>, passing its results up.</summary>
    internal List<int>[] PropertyParents { get; }

    internal static ShapesFootprint Of(ShapesGraph shapes) => new(shapes);

    /// <summary>The shapes that read triples with this predicate, and those that read every predicate.</summary>
    internal void Readers(RdfTerm predicate, List<int> into)
    {
        if (_byPredicate.TryGetValue(predicate, out List<int>? readers))
        {
            into.AddRange(readers);
        }

        into.AddRange(_anyPredicate);
    }

    /// <summary>
    /// The shapes that have <paramref name="cls"/> among their target classes,
    /// explicit or implicit (§3.1.3.2–3), none when no shape does. An
    /// <c>rdf:type</c> triple reaches them through its class's superclasses,
    /// not through <see cref="Readers"/>, unless they read <c>rdf:type</c> for
    /// another reason too.
    /// </summary>
    internal List<int>? TargetedBy(RdfTerm cls) => _byTargetClass.GetValueOrDefault(cls);

    /// <summary>Whether any shape has a target class.</summary>
    internal bool HasTargetClasses => _byTargetClass.Count > 0;

    private void Reads(int shape, RdfTerm predicate)
    {
        if (!_byPredicate.TryGetValue(predicate, out List<int>? readers))
        {
            readers = [];
            _byPredicate[predicate] = readers;
        }

        if (readers.Count == 0 || readers[^1] != shape)
        {
            readers.Add(shape);
        }
    }

    /// <summary>Indexes what the shape reads and returns the constructs that are not incremental.</summary>
    private List<ClassificationReason> Read(Shape shape)
    {
        int s = shape.Index;
        List<ClassificationReason> reasons = [];
        bool any = false;

        // Targets (§3.1.3).
        if (shape.TargetClasses.Count > 0)
        {
            foreach (RdfTerm cls in shape.TargetClasses)
            {
                if (!_byTargetClass.TryGetValue(cls, out List<int>? targeted))
                {
                    targeted = [];
                    _byTargetClass[cls] = targeted;
                }

                if (targeted.Count == 0 || targeted[^1] != s)
                {
                    targeted.Add(s);
                }
            }

            Reads(s, Vocabulary.RdfsSubClassOf);
            reasons.Add(new(Maintainability.Scoped, "sh:targetClass on rdfs:subClassOf in the data graph", "SHACL 1.2 Core §3.1.3.2"));
        }

        foreach (RdfTerm predicate in shape.TargetSubjectsOf)
        {
            Reads(s, predicate);
        }

        foreach (RdfTerm predicate in shape.TargetObjectsOf)
        {
            Reads(s, predicate);
        }

        if (shape.TargetWhere.Count > 0)
        {
            // A triple can add a node to the graph or remove one, whatever its predicate.
            any = true;
        }

        if (shape.Node.Kind == RdfTermKind.Iri)
        {
            Reads(s, Vocabulary.Shape);
        }

        // The path (§4).
        if (shape.Path is { } path)
        {
            foreach (RdfTerm label in path.Automaton.Labels)
            {
                Reads(s, label);
            }

            if (!path.Automaton.IsBounded)
            {
                reasons.Add(new(Maintainability.Scoped, "a zero-or-more or one-or-more path, " + path.Sparql, "SHACL 1.2 Core §4.5–4.6"));
            }
        }

        bool readsAtValue = false;
        foreach (Constraint constraint in shape.Constraints)
        {
            switch (constraint)
            {
                case ClassConstraint:
                    readsAtValue = true;
                    Reads(s, Vocabulary.RdfType);
                    Reads(s, Vocabulary.RdfsSubClassOf);
                    reasons.Add(new(Maintainability.Scoped, "sh:class on rdfs:subClassOf in the data graph", "SHACL 1.2 Core §7.1.1"));
                    break;

                case ClosedConstraint closed:
                    readsAtValue = true;
                    any = true;
                    if (closed.ByTypes)
                    {
                        reasons.Add(new(Maintainability.Scoped, "sh:closed sh:ByTypes on rdfs:subClassOf in the data graph", "SHACL 1.2 Core §7.9.1"));
                    }

                    break;

                case ListConstraint:
                    readsAtValue = true;
                    Reads(s, Vocabulary.RdfFirst);
                    Reads(s, Vocabulary.RdfRest);
                    reasons.Add(new(Maintainability.Scoped, "a list component, over rdf:rest*", "SHACL 1.2 Core §7.5"));
                    break;

                case RootClassConstraint:
                    readsAtValue = true;
                    Reads(s, Vocabulary.RdfsSubClassOf);
                    reasons.Add(new(Maintainability.Scoped, "sh:rootClass, over rdfs:subClassOf*", "SHACL 1.2 Core §7.9.4"));
                    break;

                case PairConstraint pair:
                    foreach (RdfTerm label in pair.Other.Automaton.Labels)
                    {
                        Reads(s, label);
                    }

                    if (!pair.Other.Automaton.IsBounded)
                    {
                        reasons.Add(new(Maintainability.Scoped, "a property pair component over a closure, " + pair.Other.Sparql, "SHACL 1.2 Core §7.6"));
                    }

                    break;

                case ReifierShapeConstraint:
                    readsAtValue = true;
                    Reads(s, Vocabulary.RdfReifies);
                    break;

                case UniqueValuesForConstraint unique:
                    readsAtValue = true;
                    foreach (RdfTerm property in unique.Properties)
                    {
                        Reads(s, property);
                    }

                    break;

                case ExtensionConstraint extension:
                    readsAtValue = true;
                    if (extension.Inner is IShaclFootprint footprint)
                    {
                        any = true;
                        if (footprint.Maintainability != Maintainability.Incremental)
                        {
                            reasons.Add(new(footprint.Maintainability, footprint.Construct, "SHACL 1.2 SPARQL-Related Features §3–4"));
                        }

                        Opaque[s] |= footprint.Maintainability == Maintainability.Full;
                    }
                    else
                    {
                        Opaque[s] = true;
                        reasons.Add(new(Maintainability.Full, "a constraint of an extension that does not state what it reads", "ADR 0171 §4"));
                    }

                    break;

                case PropertyConstraint property:
                    PropertyParents[property.Property].Add(s);
                    break;
            }

            foreach (ShapeReference _ in constraint.References)
            {
                readsAtValue = true;
                break;
            }
        }

        if ((shape.Values is not null || shape.DefaultValue is not null) && readsAtValue)
        {
            Constants[s] = true;
            reasons.Add(new(Maintainability.Full, "a constant value node, sh:values or sh:defaultValue, read beyond its term", "SHACL 1.2 Core §3.3, §5.1–5.2"));
        }

        if (shape.Cyclic)
        {
            reasons.Add(new(Maintainability.Scoped, "a recursive shape", "SHACL 1.2 Core §6.6.3, ADR 0150"));
        }

        if (any)
        {
            _anyPredicate.Add(s);
        }

        return reasons;
    }

    /// <summary>Each shape's class: the worst of its own constructs and of the shapes it reaches, dependencies first.</summary>
    private ShapesClassification Classify(ImmutableArray<Shape> shapes, List<ClassificationReason>[] own)
    {
        List<ClassificationReason>[] all = new List<ClassificationReason>[shapes.Length];
        for (int component = 0; component < Strata.ComponentCount; component++)
        {
            IReadOnlyList<int> members = Strata.Members(component);
            List<ClassificationReason> shared = [];
            foreach (int s in members)
            {
                shared.AddRange(own[s]);
                foreach (DependencyEdge<RdfTerm> edge in Graph.Outgoing(s))
                {
                    if (Strata.ComponentOf(edge.Target) == component)
                    {
                        continue;
                    }

                    foreach (ClassificationReason inherited in all[edge.Target])
                    {
                        shared.Add(new(
                            inherited.Maintainability,
                            "refers to " + Strings.NTriples(shapes[edge.Target].Node) + " by " + Strings.NTriples(edge.Label) + ": " + inherited.Construct,
                            inherited.Section));
                    }
                }
            }

            foreach (int s in members)
            {
                // A component's shapes depend on one another, so they share every reason.
                all[s] = shared;
            }
        }

        List<ShapeClassification> classified = new(shapes.Length);
        foreach (Shape shape in shapes)
        {
            Maintainability worst = Maintainability.Incremental;
            foreach (ClassificationReason reason in all[shape.Index])
            {
                if (reason.Maintainability > worst)
                {
                    worst = reason.Maintainability;
                }
            }

            List<ClassificationReason> kept = all[shape.Index].FindAll(r => r.Maintainability == worst && worst != Maintainability.Incremental);
            classified.Add(new ShapeClassification(shape.Node, worst, kept));
        }

        return new ShapesClassification(classified);
    }
}
