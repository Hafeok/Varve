// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Collections.Immutable;
using Varve.Rdf;
using Varve.Shacl.Validation;

namespace Varve.Shacl.Shapes;

/// <summary>
/// One shape of a shapes graph (SHACL 1.2 Core §3.1), as read at load. It
/// holds terms of the shapes graph and references to other shapes by index,
/// never a handle of a data source, so one shapes graph validates any number
/// of sources (ADR 0155). Built by <see cref="ShapesReader"/> and not changed
/// after.
/// </summary>
internal sealed class Shape(int index, RdfTerm node)
{
    internal int Index { get; } = index;

    /// <summary>The shape's node in the shapes graph.</summary>
    internal RdfTerm Node { get; } = node;

    /// <summary>The path of a property shape (§3.3); null for a node shape (§3.2).</summary>
    internal ShapePath? Path { get; set; }

    internal bool IsPropertyShape => Path is not null;

    internal List<RdfTerm> TargetNodes { get; } = [];

    internal List<RdfTerm> TargetClasses { get; } = [];

    internal List<RdfTerm> TargetSubjectsOf { get; } = [];

    internal List<RdfTerm> TargetObjectsOf { get; } = [];

    /// <summary>§3.1.3.6: the shapes whose conforming nodes are targets.</summary>
    internal List<int> TargetWhere { get; } = [];

    /// <summary>§3.1.3.7: whether any target is declared, so that <c>sh:shape</c> in the data is looked for.</summary>
    internal bool HasTargets =>
        TargetNodes.Count > 0 || TargetClasses.Count > 0 || TargetSubjectsOf.Count > 0 || TargetObjectsOf.Count > 0 || TargetWhere.Count > 0;

    internal bool Deactivated { get; set; }

    /// <summary>§3.1.4: <c>sh:severity</c>, <c>sh:Violation</c> by default.</summary>
    internal RdfTerm Severity { get; set; } = Vocabulary.Violation;

    /// <summary>§3.1.5: <c>sh:message</c>.</summary>
    internal ImmutableArray<RdfTerm> Messages { get; set; } = [];

    /// <summary>§3.3, §6.9.2: <c>sh:values</c> and <c>sh:defaultValue</c>, constants in Core (§5.1, §5.2).</summary>
    internal RdfTerm? Values { get; set; }

    internal RdfTerm? DefaultValue { get; set; }

    internal List<Constraint> Constraints { get; } = [];

    /// <summary>The strongly connected component of the dependency graph it belongs to (ADR 0150).</summary>
    internal int Component { get; set; } = -1;

    /// <summary>Whether its component has a cycle, so conformance to it is a fixpoint (ADR 0150).</summary>
    internal bool Cyclic { get; set; }
}

/// <summary>A reference from one shape to another, by the parameter it goes through, with ADR 0150's polarity.</summary>
internal readonly struct ShapeReference(int target, RdfTerm parameter, Polarity polarity)
{
    internal int Target { get; } = target;

    internal RdfTerm Parameter { get; } = parameter;

    internal Polarity Polarity { get; } = polarity;
}

/// <summary>ADR 0150's polarity of a reference.</summary>
internal enum Polarity
{
    Positive,
    Negative,
    Both,
    Target,
}

/// <summary>
/// One constraint of a shape: its component, the shape's triples that
/// declare it (to find the reifiers that change its severity, message or
/// activation, §3.1.4–§3.1.6), and its validator.
/// </summary>
internal abstract class Constraint
{
    private protected Constraint(RdfTerm component) => Component = component;

    /// <summary><c>sh:sourceConstraintComponent</c>.</summary>
    internal RdfTerm Component { get; }

    /// <summary>A reifier's <c>sh:severity</c>, or null for the shape's.</summary>
    internal RdfTerm? Severity { get; set; }

    /// <summary>A reifier's <c>sh:message</c>, or null for the shape's.</summary>
    internal ImmutableArray<RdfTerm>? Messages { get; set; }

    /// <summary>A reifier's <c>sh:deactivated true</c>.</summary>
    internal bool Deactivated { get; set; }

    /// <summary><c>sh:sourceConstraint</c>, for a constraint with a node of its own.</summary>
    internal virtual RdfTerm? SourceConstraint => null;

    /// <summary>The shapes this constraint checks conformance to, with polarity (ADR 0150).</summary>
    internal virtual IEnumerable<ShapeReference> References => [];

    /// <summary>
    /// Validates <paramref name="focus"/>, whose value nodes for
    /// <paramref name="shape"/> are <paramref name="values"/>, writing every
    /// result into <paramref name="sink"/>.
    /// </summary>
    internal abstract void Validate(ValidationRun run, Shape shape, Node focus, IReadOnlyList<Node> values, ResultSink sink);
}
