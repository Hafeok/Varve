// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Management.Model;

/// <summary>A template over the caller that names graphs (ADR 0166).</summary>
/// <param name="Template">An RFC 6570 level 1 template over <c>sub</c>, <c>agent</c> and <c>claim.&lt;name&gt;</c>.</param>
/// <param name="Match">Whether the expansion is a graph or a prefix of graphs.</param>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed record GraphTemplate(string Template, TemplateMatch Match);

/// <summary>
/// The graphs a grant covers: all of them, or the union of explicit graphs,
/// the default graph, prefixes and templates over the caller.
/// </summary>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed class GraphSet
{
    /// <summary>Every graph.</summary>
    public static GraphSet Everything { get; } = new() { All = true };

    /// <summary>Whether it is every graph.</summary>
    public bool All { get; init; }

    /// <summary>Whether it includes the default graph.</summary>
    public bool DefaultGraph { get; init; }

    /// <summary>Explicit named graphs.</summary>
    public IReadOnlyList<RdfTerm> Graphs { get; init; } = [];

    /// <summary>IRI prefixes of named graphs.</summary>
    public IReadOnlyList<string> Prefixes { get; init; } = [];

    /// <summary>Templates over the caller.</summary>
    public IReadOnlyList<GraphTemplate> Templates { get; init; } = [];

    /// <summary>Whether it names nothing at all.</summary>
    public bool IsEmpty => !All && !DefaultGraph && Graphs.Count == 0 && Prefixes.Count == 0 && Templates.Count == 0;
}
