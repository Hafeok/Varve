// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Management.Model;

/// <summary>One grant (ADR 0166).</summary>
/// <param name="Node">The grant's node, which names it.</param>
/// <param name="Dataset">The dataset it is on, or none for an operator grant in the fleet graph.</param>
/// <param name="Grantee">Whom it names.</param>
/// <param name="Access">What it permits.</param>
/// <param name="Graphs">The graphs it covers; every graph for admin and operator.</param>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed record GrantDeclaration(RdfTerm Node, string? Dataset, Principal Grantee, GrantAccess Access, GraphSet Graphs);

/// <summary>
/// A shapes binding (ADR 0170): its shapes graph, the graphs it validates, as a
/// grant states its graphs, and its mode. A TBox binding is a
/// <see cref="TBoxBindingDeclaration"/>.
/// </summary>
public sealed record BindingDeclaration(RdfTerm Node, BindingKind Kind, RdfTerm BoundGraph)
{
    /// <summary>The graphs a shapes binding validates; null for a TBox binding.</summary>
    public GraphSet? Validates { get; init; }

    /// <summary>A shapes binding's mode.</summary>
    public ShapesMode Mode { get; init; }

    /// <summary><c>varve:allowFullEvaluation</c>.</summary>
    [DesignDecision(typeof(TheShapesBindingVocabulary.BindingTerms), Scope = ExceptionScope.Boundary)]
    public bool AllowFullEvaluation { get; init; }

    /// <summary><c>varve:subClassOfInShapesGraph</c>.</summary>
    [DesignDecision(typeof(TheShapesBindingVocabulary.BindingTerms), Scope = ExceptionScope.Boundary)]
    public bool SubClassOfInShapesGraph { get; init; }
}

/// <summary>
/// A TBox binding (ADR 0194): the TBox graphs a query of its data graphs is
/// rewritten against, and the dataset's default regime when it states one.
/// </summary>
/// <param name="Node">The binding's node, which names it.</param>
/// <param name="TBoxGraphs">Its TBox graphs, one or more, in term order.</param>
/// <param name="DataGraphs">Its data graphs, as declared: the TBox graphs and every <c>varve:</c> graph are never among them.</param>
/// <param name="Profile">What it does.</param>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
[Contract(typeof(TheTboxBindingVocabulary.TBoxBindingTerms), Role = "a TBox binding as read")]
public sealed record TBoxBindingDeclaration(RdfTerm Node, IReadOnlyList<RdfTerm> TBoxGraphs, GraphSet DataGraphs, TBoxProfile Profile)
{
    /// <summary>The regime IRI of <c>varve:defaultEntailment</c>, or none when it is not stated.</summary>
    public RdfTerm? DefaultEntailment { get; init; }
}

/// <summary>One dataset's declarations (ADR 0163).</summary>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed class DatasetDeclaration
{
    /// <summary>The dataset's name.</summary>
    public required string Name { get; init; }

    /// <summary>The dataset's node.</summary>
    public required RdfTerm Node { get; init; }

    /// <summary>Where it is stored.</summary>
    public StorageKind Storage { get; init; } = StorageKind.File;

    /// <summary>The instance that writes it, or none for the system writer's (ADR 0163 point 12).</summary>
    public string? PlacedOn { get; init; }

    /// <summary>The declared default access scope, or none when it is not declared.</summary>
    public HistoryScope? DefaultAccessScope { get; init; }

    /// <summary>The standby opt-in, recorded for Replication.</summary>
    public bool AutomaticHandover { get; init; }

    /// <summary>Whether the dataset is declared closed: every instance closes it, and only then may it be deleted (ADR 0163).</summary>
    public bool Closed { get; init; }

    /// <summary>Where the dataset's event declarations are (ADR 0167).</summary>
    public EventDeclarationState EventDeclarations { get; init; }

    /// <summary>The dataset's grants.</summary>
    public IReadOnlyList<GrantDeclaration> Grants { get; init; } = [];

    /// <summary>The dataset's shapes bindings.</summary>
    public IReadOnlyList<BindingDeclaration> Bindings { get; init; } = [];

    /// <summary>The dataset's TBox bindings (ADR 0194), whose graph sets do not overlap.</summary>
    public IReadOnlyList<TBoxBindingDeclaration> TBoxBindings { get; init; } = [];

    /// <summary>The dataset's default regime (ADR 0194 point 3): the IRI its TBox bindings state, or none.</summary>
    public RdfTerm? DefaultEntailment { get; init; }
}

/// <summary>A finding of the code validator: what is wrong, at which node and path (ADR 0166).</summary>
/// <param name="Graph">The graph it is in.</param>
/// <param name="Focus">The node it is about, or none for the graph as a whole.</param>
/// <param name="Path">The property, or none.</param>
/// <param name="Message">What is wrong, in a sentence.</param>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed record DeclarationFinding(RdfTerm Graph, RdfTerm? Focus, RdfTerm? Path, string Message);

/// <summary>Everything the system dataset declares at one position.</summary>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed class Declarations
{
    /// <summary>No declarations: an empty system dataset.</summary>
    public static Declarations Empty { get; } = new();

    /// <summary>The datasets, by name, in ordinal order.</summary>
    public IReadOnlyDictionary<string, DatasetDeclaration> Datasets { get; init; } = new SortedDictionary<string, DatasetDeclaration>(System.StringComparer.Ordinal);

    /// <summary>The fleet's grants: the operators.</summary>
    public IReadOnlyList<GrantDeclaration> FleetGrants { get; init; } = [];

    /// <summary>The bootstrap operators the system writer recorded, as configured: agent IRIs or role values.</summary>
    public IReadOnlyList<string> Bootstrap { get; init; } = [];

    /// <summary>What the code validator refuses in them.</summary>
    public IReadOnlyList<DeclarationFinding> Findings { get; init; } = [];
}
