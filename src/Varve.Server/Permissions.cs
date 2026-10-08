// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Varve.Protocol;
using Varve.Protocol.Model;

namespace Varve.Server;

/// <summary>Which permission a policy asks for (ADRs 0037, 0106), in cumulative order.</summary>
internal enum Permission : byte
{
    Read = 0,
    Write = 1,
    Admin = 2,
    ServerAdmin = 3,
}

/// <summary>The requirement behind each of <see cref="DatasetPermissions"/>' policy names.</summary>
internal sealed class DatasetPermission(Permission permission) : IAuthorizationRequirement
{
    internal Permission Permission { get; } = permission;
}

/// <summary>
/// Decides a <see cref="DatasetPermission"/> (ADRs 0037, 0091, 0106): the
/// caller is authenticated, and one of its role claims is a value the
/// configuration maps to that permission, or to a higher one. A dataset
/// permission is decided on the dataset the request names, and a server
/// admin holds every one of them on every dataset; the server-admin
/// permission is decided on no dataset. No dataset, no dataset permission.
/// </summary>
internal sealed class DatasetPermissionHandler(AuthSettings settings) : AuthorizationHandler<DatasetPermission>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DatasetPermission requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        HashSet<string> values = new(StringComparer.Ordinal);
        values.UnionWith(settings.Server.Admin);

        if (requirement.Permission != Permission.ServerAdmin)
        {
            if (context.Resource is not DatasetName name || !settings.Datasets.TryGetValue(name.Value, out PermissionSettings? granted))
            {
                granted = null;
            }

            if (granted is not null)
            {
                values.UnionWith(granted.Admin);

                if (requirement.Permission <= Permission.Write)
                {
                    values.UnionWith(granted.Write);
                }

                if (requirement.Permission == Permission.Read)
                {
                    values.UnionWith(granted.Read);
                }

                // A scoped grant passes the dataset-level policy for its
                // permission; the scope then bounds the request (ADR 0107).
                foreach (GrantSettings grant in granted.Grants)
                {
                    if (grant.Claim is { } claim && (requirement.Permission == Permission.Read || (requirement.Permission == Permission.Write && grant.Permission == "write")))
                    {
                        values.Add(claim);
                    }
                }
            }
        }

        if (HoldsAny(context.User, settings.RoleClaimType, values))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <summary>Whether one of the caller's role claims is a value in <paramref name="values"/>.</summary>
    internal static bool HoldsAny(System.Security.Claims.ClaimsPrincipal user, string roleClaimType, HashSet<string> values)
    {
        foreach (System.Security.Claims.Claim claim in user.FindAll(roleClaimType))
        {
            if (Grants(claim.Value, values))
            {
                return true;
            }
        }

        return false;
    }

    // A role claim is a string (Entra's app roles, Keycloak's), or a JSON
    // object whose property names are the roles (Zitadel's project roles).
    internal static bool Grants(string value, HashSet<string> granted)
    {
        if (!value.StartsWith('{'))
        {
            return granted.Contains(value);
        }

        try
        {
            using System.Text.Json.JsonDocument roles = System.Text.Json.JsonDocument.Parse(value);

            foreach (System.Text.Json.JsonProperty role in roles.RootElement.EnumerateObject())
            {
                if (granted.Contains(role.Name))
                {
                    return true;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return false;
    }
}
