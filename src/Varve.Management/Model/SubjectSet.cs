// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Management.Model;

/// <summary>
/// A link form of a subject set (ADR 0234): the data subjects with
/// <c>(s <see cref="Predicate"/> target)</c>, the target being the caller's
/// agent, or a template's expansion when <see cref="Target"/> is given.
/// </summary>
/// <param name="Predicate">The link's predicate.</param>
/// <param name="Target">The target as a template over the caller, or none for the caller's agent.</param>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed record SubjectLinkDeclaration(RdfTerm Predicate, GraphTemplate? Target);

/// <summary>
/// The data subjects a grant covers (ADR 0234 point 2): all of them, or the
/// union of explicit subjects, prefixes, templates over the caller, classes
/// and links, with where a write grant may create rows.
/// </summary>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed class SubjectSet
{
    /// <summary>Every subject: a grant without subject terms.</summary>
    public static SubjectSet Everything { get; } = new() { All = true };

    /// <summary>Whether it is every subject.</summary>
    public bool All { get; init; }

    /// <summary>Explicit subjects.</summary>
    public IReadOnlyList<RdfTerm> Subjects { get; init; } = [];

    /// <summary>IRI prefixes of subjects.</summary>
    public IReadOnlyList<string> Prefixes { get; init; } = [];

    /// <summary>Templates over the caller naming a subject or a prefix of subjects.</summary>
    public IReadOnlyList<GraphTemplate> Templates { get; init; } = [];

    /// <summary>Classes whose instances are in the set.</summary>
    public IReadOnlyList<RdfTerm> Classes { get; init; } = [];

    /// <summary>Links whose sources are in the set.</summary>
    public IReadOnlyList<SubjectLinkDeclaration> Links { get; init; } = [];

    /// <summary>Prefixes under which a write grant may create rows (ADR 0236, W3).</summary>
    public IReadOnlyList<string> CreateUnder { get; init; } = [];

    /// <summary>Templates over the caller under whose expansion a write grant may create rows.</summary>
    public IReadOnlyList<GraphTemplate> CreateUnderTemplates { get; init; } = [];
}
