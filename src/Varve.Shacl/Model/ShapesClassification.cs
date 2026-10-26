// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Shacl.Model;

/// <summary>How a shape's affected focus nodes are found from a delta (ADR 0171).</summary>
public enum Maintainability
{
    /// <summary>By a bounded local rule: a fixed number of lookups per changed triple.</summary>
    Incremental,

    /// <summary>By a closure whose size depends on the data.</summary>
    Scoped,

    /// <summary>Not bounded: every target of the shape is affected by every change to the data graph.</summary>
    Full,
}

/// <summary>One construct that decides a shape's class, with the section that defines it.</summary>
public sealed class ClassificationReason
{
    internal ClassificationReason(Maintainability maintainability, string construct, string section)
    {
        Maintainability = maintainability;
        Construct = construct;
        Section = section;
    }

    /// <summary>The class this construct puts the shape in.</summary>
    public Maintainability Maintainability { get; }

    /// <summary>The construct, such as <c>sh:zeroOrMorePath</c>, or a reference to another shape.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    public string Construct { get; }

    /// <summary>The section that defines it, in SHACL 1.2 Core or SHACL-SPARQL.</summary>
    [DesignDecision(typeof(AMinimalPublicShaclApi.ShaclModelPrimitives), Scope = ExceptionScope.Boundary)]
    public string Section { get; }
}

/// <summary>A shape's class, and every construct that puts it there.</summary>
public sealed class ShapeClassification
{
    internal ShapeClassification(RdfTerm shape, Maintainability maintainability, IReadOnlyList<ClassificationReason> reasons)
    {
        Shape = shape;
        Maintainability = maintainability;
        Reasons = reasons;
    }

    /// <summary>The shape's node in the shapes graph.</summary>
    public RdfTerm Shape { get; }

    /// <summary>The worst class among its constructs and the shapes it reaches.</summary>
    public Maintainability Maintainability { get; }

    /// <summary>The constructs of that class, its own and those it inherits by reference; empty for an incremental shape.</summary>
    public IReadOnlyList<ClassificationReason> Reasons { get; }
}

/// <summary>
/// The classification of every shape of a shapes graph (ADR 0171), computed
/// from the shapes graph alone, so no data can change it.
/// </summary>
public sealed class ShapesClassification
{
    internal ShapesClassification(IReadOnlyList<ShapeClassification> shapes)
    {
        Shapes = shapes;
        Maintainability worst = Maintainability.Incremental;
        foreach (ShapeClassification shape in shapes)
        {
            if (shape.Maintainability > worst)
            {
                worst = shape.Maintainability;
            }
        }

        Worst = worst;
    }

    /// <summary>Every shape, in the shapes graph's reading order.</summary>
    public IReadOnlyList<ShapeClassification> Shapes { get; }

    /// <summary>The worst class of any shape.</summary>
    public Maintainability Worst { get; }
}
