// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Shacl.Model;

/// <summary>
/// One validation result (SHACL 1.2 Core §6.8.2), as typed values. A blank
/// node of the data is the term its data source gives (ADR 0154).
/// </summary>
public sealed class ShaclResult
{
    internal ShaclResult(
        RdfTerm focusNode,
        RdfTerm? path,
        bool hasPath,
        RdfTerm? value,
        RdfTerm sourceShape,
        RdfTerm sourceConstraintComponent,
        RdfTerm? sourceConstraint,
        RdfTerm severity,
        IReadOnlyList<RdfTerm> messages)
    {
        FocusNode = focusNode;
        Path = path;
        HasPath = hasPath;
        Value = value;
        SourceShape = sourceShape;
        SourceConstraintComponent = sourceConstraintComponent;
        SourceConstraint = sourceConstraint;
        Severity = severity;
        Messages = messages;
    }

    /// <summary><c>sh:focusNode</c>.</summary>
    public RdfTerm FocusNode { get; }

    /// <summary>
    /// <c>sh:resultPath</c> when it is a predicate path, its IRI; null when
    /// the result has no path or a complex one, whose structure the report
    /// graph carries (<see cref="HasPath"/> tells them apart).
    /// </summary>
    public RdfTerm? Path { get; }

    /// <summary>Whether the result has a <c>sh:resultPath</c>.</summary>
    public bool HasPath { get; }

    /// <summary><c>sh:value</c>, when the result has one.</summary>
    public RdfTerm? Value { get; }

    /// <summary><c>sh:sourceShape</c>.</summary>
    public RdfTerm SourceShape { get; }

    /// <summary><c>sh:sourceConstraintComponent</c>.</summary>
    public RdfTerm SourceConstraintComponent { get; }

    /// <summary><c>sh:sourceConstraint</c>, for a constraint that has a node of its own (SHACL-SPARQL).</summary>
    public RdfTerm? SourceConstraint { get; }

    /// <summary><c>sh:resultSeverity</c>.</summary>
    public RdfTerm Severity { get; }

    /// <summary><c>sh:resultMessage</c>, the messages of the shape or constraint.</summary>
    public IReadOnlyList<RdfTerm> Messages { get; }
}
