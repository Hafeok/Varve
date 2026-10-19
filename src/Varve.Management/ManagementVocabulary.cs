// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Management;

/// <summary>
/// The management vocabulary and the system dataset's graph names (ADR 0166).
/// Every term is in <c>https://w3id.org/varve/ns#</c>.
/// </summary>
[Contract(typeof(VarveManagementAndTheVocabulary.ManagementVocabulary), Role = "the management plane's terms and graph names")]
public static class ManagementVocabulary
{
    /// <summary>The namespace.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string Namespace = "https://w3id.org/varve/ns#";

    /// <summary>The fleet graph's IRI.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string FleetGraphIri = Namespace + "manage/fleet";

    /// <summary>The bootstrap graph's IRI, written only by the system writer.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string BootstrapGraphIri = Namespace + "manage/bootstrap";

    /// <summary>The prefix of every dataset's declaration graph.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string DatasetGraphPrefix = Namespace + "manage/dataset/";

    /// <summary>The prefix of every system graph the management plane owns.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string ManagePrefix = Namespace + "manage/";

    /// <summary>The fleet graph.</summary>
    public static RdfTerm FleetGraph { get; } = Term("manage/fleet");

    /// <summary>The bootstrap graph.</summary>
    public static RdfTerm BootstrapGraph { get; } = Term("manage/bootstrap");

    // Classes.
    internal static readonly RdfTerm Dataset = Term("Dataset");
    internal static readonly RdfTerm Grant = Term("Grant");
    internal static readonly RdfTerm Agent = Term("Agent");
    internal static readonly RdfTerm Role = Term("Role");
    internal static readonly RdfTerm Group = Term("Group");
    internal static readonly RdfTerm GraphTemplate = Term("GraphTemplate");
    internal static readonly RdfTerm Binding = Term("Binding");
    internal static readonly RdfTerm ShapesBinding = Term("ShapesBinding");
    internal static readonly RdfTerm TBoxBinding = Term("TBoxBinding");
    internal static readonly RdfTerm ClassifierBinding = Term("ClassifierBinding");
    internal static readonly RdfTerm KeyStoreBinding = Term("KeyStoreBinding");

    // Properties.
    internal static readonly RdfTerm DatasetName = Term("datasetName");
    internal static readonly RdfTerm Storage = Term("storage");
    internal static readonly RdfTerm PlacedOn = Term("placedOn");
    internal static readonly RdfTerm DefaultAccessScope = Term("defaultAccessScope");
    internal static readonly RdfTerm AutomaticHandover = Term("automaticHandover");
    internal static readonly RdfTerm ErasureMode = Term("erasureMode");
    internal static readonly RdfTerm Grantee = Term("grantee");
    internal static readonly RdfTerm Permission = Term("permission");
    internal static readonly RdfTerm AllGraphs = Term("allGraphs");
    internal static readonly RdfTerm Graph = Term("graph");
    internal static readonly RdfTerm GraphPrefix = Term("graphPrefix");
    internal static readonly RdfTerm GraphTemplateProperty = Term("graphTemplate");
    internal static readonly RdfTerm ClaimValue = Term("claimValue");
    internal static readonly RdfTerm ClaimType = Term("claimType");
    internal static readonly RdfTerm Template = Term("template");
    internal static readonly RdfTerm Match = Term("match");
    internal static readonly RdfTerm BoundGraph = Term("boundGraph");
    internal static readonly RdfTerm BootstrapOperator = Term("bootstrapOperator");

    // Individuals.
    internal static readonly RdfTerm File = Term("File");
    internal static readonly RdfTerm Memory = Term("Memory");
    internal static readonly RdfTerm ObjectStorage = Term("ObjectStorage");
    internal static readonly RdfTerm AllHistory = Term("AllHistory");
    internal static readonly RdfTerm Current = Term("Current");
    internal static readonly RdfTerm Read = Term("read");
    internal static readonly RdfTerm Write = Term("write");
    internal static readonly RdfTerm Admin = Term("admin");
    internal static readonly RdfTerm Operator = Term("operator");
    internal static readonly RdfTerm DefaultGraph = Term("defaultGraph");
    internal static readonly RdfTerm Exact = Term("Exact");
    internal static readonly RdfTerm Prefix = Term("Prefix");

    // Outside varve:.
    internal static readonly RdfTerm Type = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    internal const string XsdBoolean = "http://www.w3.org/2001/XMLSchema#boolean";

    /// <summary>
    /// The event-catalogue terms of ADR 0144, which a management graph holds
    /// only from part 2 on (ADR 0167): until then they are refused.
    /// </summary>
    internal static readonly FrozenSet<string> EventTerms = new[]
    {
        "EventType", "EventField", "eventName", "eventClass", "eventVersion", "eventSelect", "eventSubject", "eventRetired",
        "eventField", "fieldName", "fieldDatatype", "fieldRequired", "fieldAs", "fieldKeepLanguage", "sourceGraph",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Every varve: term a declaration may use, by local name.</summary>
    internal static readonly FrozenSet<string> Known = new[]
    {
        "Dataset", "Grant", "Agent", "Role", "Group", "GraphTemplate", "ShapesBinding", "TBoxBinding",
        "datasetName", "storage", "placedOn", "defaultAccessScope", "automaticHandover",
        "grantee", "permission", "allGraphs", "graph", "graphPrefix", "graphTemplate",
        "claimValue", "claimType", "template", "match", "boundGraph",
        "File", "Memory", "ObjectStorage", "AllHistory", "Current", "read", "write", "admin", "operator", "defaultGraph", "Exact", "Prefix",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The terms reserved for later milestones, refused with the reason.</summary>
    internal static readonly FrozenDictionary<string, string> Reserved = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Binding"] = "varve:Binding is abstract; declare a varve:ShapesBinding or a varve:TBoxBinding",
        ["ClassifierBinding"] = "varve:ClassifierBinding is reserved for erasure and row-level access (#14)",
        ["KeyStoreBinding"] = "varve:KeyStoreBinding is reserved for erasure and row-level access (#14)",
        ["erasureMode"] = "varve:erasureMode is reserved for erasure mode (#14)",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>A dataset's declaration graph.</summary>
    /// <exception cref="ArgumentException"><paramref name="dataset"/> is not a dataset name.</exception>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static RdfTerm DatasetGraph(string dataset)
    {
        if (!IsDatasetName(dataset))
        {
            throw new ArgumentException("A dataset name is 1 to 63 characters of [A-Za-z0-9._-], starting with a letter or a digit.", nameof(dataset));
        }

        // Every character a name may hold is unreserved (RFC 3986): the name
        // is its own percent-encoding.
        return RdfTerm.Iri(Encoding.UTF8.GetBytes(DatasetGraphPrefix + dataset));
    }

    /// <summary>The dataset a declaration graph declares, when it is one.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static bool TryGetDataset(RdfTerm graph, [NotNullWhen(true)] out string? dataset)
    {
        ArgumentNullException.ThrowIfNull(graph);
        dataset = null;

        if (graph.Kind != RdfTermKind.Iri)
        {
            return false;
        }

        string iri = Encoding.UTF8.GetString(graph.Lexical);

        if (!iri.StartsWith(DatasetGraphPrefix, StringComparison.Ordinal) || !IsDatasetName(iri[DatasetGraphPrefix.Length..]))
        {
            return false;
        }

        dataset = iri[DatasetGraphPrefix.Length..];
        return true;
    }

    /// <summary>Whether a graph is one the management plane owns: the fleet's, the bootstrap's, or a dataset's.</summary>
    public static bool IsManagementGraph(RdfTerm graph) =>
        graph is { Kind: RdfTermKind.Iri } && (graph.Equals(FleetGraph) || graph.Equals(BootstrapGraph) || TryGetDataset(graph, out _));

    /// <summary>A dataset name: 1 to 63 characters of <c>[A-Za-z0-9._-]</c>, starting with a letter or a digit (ADR 0093).</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static bool IsDatasetName([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 63 || !char.IsAsciiLetterOrDigit(name[0]))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The local name of a varve: IRI, or null for any other term.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    internal static string? LocalName(RdfTerm term)
    {
        if (term.Kind != RdfTermKind.Iri)
        {
            return null;
        }

        string iri = Encoding.UTF8.GetString(term.Lexical);
        return iri.StartsWith(Namespace, StringComparison.Ordinal) ? iri[Namespace.Length..] : null;
    }

    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    internal static RdfTerm Term(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes(Namespace + local));
}
