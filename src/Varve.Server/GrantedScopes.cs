// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Rdf;

namespace Varve.Server;

/// <summary>
/// The host's <see cref="IAccessScopes"/> (ADR 0107): a caller's readable
/// graphs are the union of the graph sets of every <c>read</c> and
/// <c>write</c> grant one of its role claims matches, its writable graphs
/// the union of the <c>write</c> grants'; the 7a lists are the <c>all</c>
/// case; a dataset admin or a server admin reads and writes every graph.
/// The scopes are built once from the settings, so a request costs a few
/// set lookups.
/// </summary>
internal sealed class GrantedScopes : IAccessScopes
{
    private readonly AuthSettings _settings;
    private readonly Dictionary<string, Resolved> _datasets = new(StringComparer.Ordinal);

    internal GrantedScopes(AuthSettings settings)
    {
        _settings = settings;

        foreach ((string name, PermissionSettings permissions) in settings.Datasets)
        {
            _datasets[name] = Resolved.Of(permissions);
        }
    }

    public CallerScope ScopesOf(ClaimsPrincipal caller, DatasetName dataset)
    {
        HashSet<string> admins = new(_settings.Server.Admin, StringComparer.Ordinal);

        if (!_datasets.TryGetValue(dataset.Value, out Resolved? resolved))
        {
            // No grants on the dataset: a server admin alone reaches it.
            return DatasetPermissionHandler.HoldsAny(caller, _settings.RoleClaimType, admins)
                ? CallerScope.Everything
                : new CallerScope(GraphScope.None, GraphScope.None, AdminAccess.None);
        }

        admins.UnionWith(resolved.Admin);

        if (DatasetPermissionHandler.HoldsAny(caller, _settings.RoleClaimType, admins))
        {
            return CallerScope.Everything;
        }

        GraphScope readable = GraphScope.None;
        GraphScope writable = GraphScope.None;

        foreach (Claim claim in caller.FindAll(_settings.RoleClaimType))
        {
            foreach ((string value, GraphScope scope, bool write) in resolved.Grants)
            {
                if (DatasetPermissionHandler.Grants(claim.Value, new HashSet<string>([value], StringComparer.Ordinal)))
                {
                    readable = readable.Union(scope);

                    if (write)
                    {
                        writable = writable.Union(scope);
                    }
                }
            }
        }

        return new CallerScope(readable, writable, AdminAccess.None);
    }

    /// <summary>A dataset's grants with their scopes built.</summary>
    private sealed class Resolved(HashSet<string> admin, List<(string Claim, GraphScope Scope, bool Write)> grants)
    {
        internal HashSet<string> Admin { get; } = admin;

        internal List<(string Claim, GraphScope Scope, bool Write)> Grants { get; } = grants;

        internal static Resolved Of(PermissionSettings permissions)
        {
            List<(string, GraphScope, bool)> grants = [];

            foreach (string claim in permissions.Read)
            {
                grants.Add((claim, GraphScope.All, false));
            }

            foreach (string claim in permissions.Write)
            {
                grants.Add((claim, GraphScope.All, true));
            }

            foreach (GrantSettings grant in permissions.Grants)
            {
                if (grant.Claim is null)
                {
                    continue;
                }

                List<RdfTerm> graphs = [];
                DefaultGraphAccess defaultGraph = DefaultGraphAccess.Excluded;

                foreach (string graph in grant.Graphs)
                {
                    if (graph == "default")
                    {
                        defaultGraph = DefaultGraphAccess.Included;
                    }
                    else
                    {
                        graphs.Add(RdfTerm.Iri(Encoding.UTF8.GetBytes(graph)));
                    }
                }

                grants.Add((grant.Claim, GraphScope.Of([.. graphs], [.. grant.GraphPrefixes], defaultGraph), grant.Permission == "write"));
            }

            return new Resolved(new HashSet<string>(permissions.Admin, StringComparer.Ordinal), grants);
        }
    }
}
