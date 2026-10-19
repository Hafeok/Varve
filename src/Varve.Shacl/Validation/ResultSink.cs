// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Collections.Immutable;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Shacl.Shapes;

namespace Varve.Shacl.Validation;

/// <summary>One validation result as the validator holds it, before it becomes a <c>ShaclResult</c>.</summary>
internal readonly struct ResultRecord(
    Node focus,
    Shape shape,
    RdfTerm component,
    RdfTerm severity,
    ImmutableArray<RdfTerm> messages,
    ShapePath? path,
    Node? value,
    RdfTerm? sourceConstraint)
{
    internal Node Focus { get; } = focus;

    internal Shape Shape { get; } = shape;

    internal RdfTerm Component { get; } = component;

    internal RdfTerm Severity { get; } = severity;

    internal ImmutableArray<RdfTerm> Messages { get; } = messages;

    internal ShapePath? Path { get; } = path;

    internal Node? Value { get; } = value;

    internal RdfTerm? SourceConstraint { get; } = sourceConstraint;
}

/// <summary>
/// Where results go: the report's list, or — for a conformance check
/// (§6.7) — a flag that a disallowed result was produced, kept apart from
/// the surrounding validation.
/// </summary>
internal sealed class ResultSink
{
    private readonly List<ResultRecord>? _results;
    private readonly HashSet<RdfTerm>? _disallowed;

    private ResultSink(List<ResultRecord>? results, HashSet<RdfTerm>? disallowed)
    {
        _results = results;
        _disallowed = disallowed;
    }

    /// <summary>A sink that keeps every result, for the report.</summary>
    internal static ResultSink Collecting(List<ResultRecord> results) => new(results, null);

    /// <summary>A sink that only notes whether a result had a disallowed severity, for conformance checking.</summary>
    internal static ResultSink Checking(HashSet<RdfTerm> disallowed) => new(null, disallowed);

    /// <summary>Whether a result with a disallowed severity reached this checking sink.</summary>
    internal bool Disallowed { get; private set; }

    /// <summary>A result of <paramref name="constraint"/> at <paramref name="shape"/>, with the shape's path unless one is given.</summary>
    [DesignDecision(typeof(ShaclHotPathScope.ResultsCostTheirRecord), Scope = ExceptionScope.HotPath)]
    internal void Add(Shape shape, Constraint constraint, Node focus, Node? value, ShapePath? path = null, ImmutableArray<RdfTerm>? messages = null)
    {
        RdfTerm severity = constraint.Severity ?? shape.Severity;
        if (_disallowed is not null)
        {
            Disallowed |= _disallowed.Contains(severity);
            return;
        }

        _results!.Add(new ResultRecord(
            focus,
            shape,
            constraint.Component,
            severity,
            messages ?? constraint.Messages ?? shape.Messages,
            path ?? shape.Path,
            value,
            constraint.SourceConstraint));
    }

    /// <summary>A result produced elsewhere — by a nested property shape — passed through as it is.</summary>
    [DesignDecision(typeof(ShaclHotPathScope.ResultsCostTheirRecord), Scope = ExceptionScope.HotPath)]
    internal void Add(in ResultRecord record)
    {
        if (_disallowed is not null)
        {
            Disallowed |= _disallowed.Contains(record.Severity);
            return;
        }

        _results!.Add(record);
    }
}
