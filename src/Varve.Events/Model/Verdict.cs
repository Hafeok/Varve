// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Events.Model;

/// <summary>One incompatible change to a field (docs/spec/events.md §7.2).</summary>
/// <param name="Field">The field's name.</param>
/// <param name="Change">The kind of change: <c>removed</c>, <c>type</c>, <c>required</c> or <c>optional</c>.</param>
[DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public readonly record struct FieldChange(string Field, string Change);

/// <summary>What the check of a declaration change decided.</summary>
public sealed class DeclarationVerdict
{
    private DeclarationVerdict(bool accepted, string? problemType, RdfTerm? declaration, string? reason, string? detail, IReadOnlyList<FieldChange> fields)
    {
        Accepted = accepted;
        ProblemType = problemType;
        Declaration = declaration;
        Reason = reason;
        Detail = detail;
        Fields = fields;
    }

    /// <summary>The problem type of a declaration that is not well formed.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string InvalidType = "https://w3id.org/varve/problems/event-declaration-invalid";

    /// <summary>The problem type of an incompatible change at an unchanged version.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string IncompatibleType = "https://w3id.org/varve/problems/event-schema-incompatible";

    internal static readonly DeclarationVerdict Accept = new(true, null, null, null, null, []);

    /// <summary>Whether the change may commit.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public bool Accepted { get; }

    /// <summary>The refusal's problem type: <see cref="InvalidType"/> or <see cref="IncompatibleType"/>.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string? ProblemType { get; }

    /// <summary>The declaration refused: the type node written, or the graph.</summary>
    public RdfTerm? Declaration { get; }

    /// <summary>The reason of §7.1, for <see cref="InvalidType"/>.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string? Reason { get; }

    /// <summary>What is wrong, for the problem's <c>detail</c>; it names nothing the caller did not write.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string? Detail { get; }

    /// <summary>The incompatible fields, in field-name order, for <see cref="IncompatibleType"/>.</summary>
    public IReadOnlyList<FieldChange> Fields { get; }

    internal static DeclarationVerdict Invalid(DeclarationProblem problem) =>
        new(false, InvalidType, problem.Declaration, problem.Reason, problem.Detail, []);

    internal static DeclarationVerdict Incompatible(RdfTerm declaration, IReadOnlyList<FieldChange> fields) =>
        new(false, IncompatibleType, declaration, null, "the change is incompatible with the declared version; state a higher version", fields);
}
