// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Iri;
using Varve.Management.Model;
using Varve.Rdf;

namespace Varve.Management;

/// <summary>
/// The grants in force at one management position, compiled once and read by
/// every request at that position (ADR 0166 point 5). Immutable.
/// </summary>
[Contract(typeof(VarveManagementAndTheVocabulary.GrantTablePerPosition), Role = "the grants in force at one management position")]
public sealed class GrantTable
{
    private readonly string[] _bootstrap;
    private readonly Dictionary<string, List<GrantDeclaration>> _byDataset = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RdfTerm[]> _datasetPublic = new(StringComparer.Ordinal);

    private GrantTable(ManagementPosition position, Declarations declarations, string[] bootstrap)
    {
        Position = position;
        Declarations = declarations;
        _bootstrap = bootstrap;

        foreach ((string name, DatasetDeclaration dataset) in declarations.Datasets)
        {
            _byDataset[name] = [.. dataset.Grants];
            _datasetPublic[name] = [.. DatasetPublicGraphs(dataset)];
        }
    }

    /// <summary>No grants at position 0, with the bootstrap operators alone.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static GrantTable Empty(IReadOnlyList<string> bootstrap) => Compile(Declarations.Empty, default, bootstrap);

    /// <summary>The management position the table is of.</summary>
    public ManagementPosition Position { get; }

    /// <summary>The declarations it was compiled from.</summary>
    public Declarations Declarations { get; }

    /// <summary>
    /// Compiles the grants of <paramref name="declarations"/> at
    /// <paramref name="position"/>. <paramref name="bootstrap"/> are the
    /// bootstrap operators, agent IRIs or role values (ADR 0163 point 7).
    /// </summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static GrantTable Compile(Declarations declarations, ManagementPosition position, IReadOnlyList<string> bootstrap)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(bootstrap);
        return new GrantTable(position, declarations, [.. bootstrap]);
    }

    /// <summary>Whether <paramref name="caller"/> is an operator: a bootstrap operator, or granted operator in the fleet graph.</summary>
    public bool IsOperator(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        foreach (string principal in _bootstrap)
        {
            if (IsAgentIri(principal) ? string.Equals(caller.Agent, principal, StringComparison.Ordinal) : caller.Holds(caller.RoleClaimType, principal))
            {
                return true;
            }
        }

        foreach (GrantDeclaration grant in Declarations.FleetGrants)
        {
            if (grant.Access == GrantAccess.Operator && Matches(grant.Grantee, caller))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="caller"/> administers <paramref name="dataset"/>: an operator, or granted admin on it.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public bool IsAdmin(Caller caller, string dataset)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (IsOperator(caller))
        {
            return true;
        }

        if (_byDataset.TryGetValue(dataset, out List<GrantDeclaration>? grants))
        {
            foreach (GrantDeclaration grant in grants)
            {
                if (grant.Access == GrantAccess.Admin && Matches(grant.Grantee, caller))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether any grant on <paramref name="dataset"/> names <paramref name="caller"/>, or it administers it.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public bool Reaches(Caller caller, string dataset)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (IsOperator(caller))
        {
            return true;
        }

        if (_byDataset.TryGetValue(dataset, out List<GrantDeclaration>? grants))
        {
            foreach (GrantDeclaration grant in grants)
            {
                if (Matches(grant.Grantee, caller))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="caller"/> may write <paramref name="dataset"/>: it administers it, or a write grant on it names it.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public bool Writes(Caller caller, string dataset)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (IsAdmin(caller, dataset))
        {
            return true;
        }

        if (_byDataset.TryGetValue(dataset, out List<GrantDeclaration>? grants))
        {
            foreach (GrantDeclaration grant in grants)
            {
                if (grant.Access == GrantAccess.Write && Matches(grant.Grantee, caller))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The caller's scope on a dataset: everything for an operator or an
    /// admin; otherwise the union of the read and write grants' graphs, and of
    /// the write grants' (ADR 0107's rule), templates expanded for the caller.
    /// A dataset the table does not declare gives none.
    /// </summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public CallerScope ScopesOf(Caller caller, string dataset)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (IsAdmin(caller, dataset))
        {
            return CallerScope.Everything;
        }

        GraphScope readable = GraphScope.None;
        GraphScope writable = GraphScope.None;

        if (_byDataset.TryGetValue(dataset, out List<GrantDeclaration>? grants))
        {
            foreach (GrantDeclaration grant in grants)
            {
                if (grant.Access is not (GrantAccess.Read or GrantAccess.Write) || !Matches(grant.Grantee, caller))
                {
                    continue;
                }

                GraphScope scope = ScopeOf(grant.Graphs, caller);
                readable = readable.Union(scope);

                if (grant.Access == GrantAccess.Write)
                {
                    writable = writable.Union(scope);
                }
            }
        }

        // A TBox graph is read by every reader of the dataset (ADR 0190 point 3); writing it still needs a write grant.
        if (_datasetPublic.TryGetValue(dataset, out RdfTerm[]? datasetPublic))
        {
            readable = readable.WithDatasetPublic(datasetPublic);
        }

        return new CallerScope(readable, writable, AdminAccess.None);
    }

    /// <summary>
    /// The graphs <paramref name="dataset"/>'s TBox bindings make
    /// dataset-public (ADR 0190 point 3): their TBox graphs, in term order,
    /// each once.
    /// </summary>
    public static IReadOnlyList<RdfTerm> DatasetPublicGraphs(DatasetDeclaration dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        SortedSet<RdfTerm> graphs = new(TermOrder.Instance);

        foreach (TBoxBindingDeclaration binding in dataset.TBoxBindings)
        {
            graphs.UnionWith(binding.TBoxGraphs);
        }

        return [.. graphs];
    }

    private static GraphScope ScopeOf(GraphSet set, Caller caller)
    {
        if (set.All)
        {
            return GraphScope.All;
        }

        List<RdfTerm> graphs = [.. set.Graphs];
        List<string> prefixes = [.. set.Prefixes];

        foreach (GraphTemplate template in set.Templates)
        {
            if (GraphTemplates.Expand(template, caller) is not { } expanded)
            {
                continue;
            }

            if (template.Match == TemplateMatch.Prefix)
            {
                prefixes.Add(expanded);
            }
            else
            {
                graphs.Add(RdfTerm.Iri(Encoding.UTF8.GetBytes(expanded)));
            }
        }

        return GraphScope.Of([.. graphs], [.. prefixes], set.DefaultGraph ? DefaultGraphAccess.Included : DefaultGraphAccess.Excluded);
    }

    private static bool Matches(Principal principal, Caller caller) => principal.Kind switch
    {
        PrincipalKind.Agent => string.Equals(caller.Agent, principal.Value, StringComparison.Ordinal),
        PrincipalKind.Role => caller.Holds(caller.RoleClaimType, principal.Value),
        _ => caller.Holds(principal.ClaimType, principal.Value),
    };

    // A bootstrap operator is an agent when it is an absolute IRI, and a role value otherwise.
    private static bool IsAgentIri(string principal) =>
        principal.Contains(':', StringComparison.Ordinal) && IriRef.IsAbsolute(Encoding.UTF8.GetBytes(principal));
}
