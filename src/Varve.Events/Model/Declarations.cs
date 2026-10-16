// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Events.Model;

/// <summary>A reason a catalogue graph's content is not a well-formed declaration (docs/spec/events.md §7.1).</summary>
/// <param name="Declaration">The type node, field node or graph in question.</param>
/// <param name="Reason">One of the reasons of §7.1: <c>graph</c>, <c>blank-node</c>, <c>term</c>, <c>select</c>, <c>field</c>, <c>class-taken</c>, <c>name-taken</c>, <c>removed</c>, <c>version</c>.</param>
/// <param name="Detail">What is wrong, for the problem's <c>detail</c>.</param>
[DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public readonly record struct DeclarationProblem(RdfTerm Declaration, string Reason, string Detail);

/// <summary>The declarations a set of catalogue graphs holds, and what in them is not well formed.</summary>
public sealed class DeclarationSet
{
    internal DeclarationSet(IReadOnlyList<EventTypeDeclaration> types, IReadOnlyList<DeclarationProblem> problems)
    {
        Types = types;
        Problems = problems;
    }

    /// <summary>The well-formed declarations, in short-name order, then by type node.</summary>
    public IReadOnlyList<EventTypeDeclaration> Types { get; }

    /// <summary>What is not well formed, in graph and node order.</summary>
    public IReadOnlyList<DeclarationProblem> Problems { get; }
}
