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

/// <summary>Which of the three permissions a policy asks for (ADR 0037).</summary>
internal enum Permission : byte
{
    Read = 0,
    Write = 1,
    Admin = 2,
}

/// <summary>The requirement behind each of <see cref="DatasetPermissions"/>' policy names.</summary>
internal sealed class DatasetPermission(Permission permission) : IAuthorizationRequirement
{
    internal Permission Permission { get; } = permission;
}

/// <summary>
/// Decides a <see cref="DatasetPermission"/> on the dataset the request names
/// (ADRs 0037, 0091): the caller is authenticated, and one of its role
/// claims is a value the configuration maps to that permission, or to a
/// higher one, on that dataset. No dataset, no permission.
/// </summary>
internal sealed class DatasetPermissionHandler(AuthSettings settings) : AuthorizationHandler<DatasetPermission>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DatasetPermission requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || context.Resource is not DatasetName name
            || !settings.Datasets.TryGetValue(name.Value, out PermissionSettings? granted))
        {
            return Task.CompletedTask;
        }

        HashSet<string> values = new(StringComparer.Ordinal);
        values.UnionWith(granted.Admin);

        if (requirement.Permission <= Permission.Write)
        {
            values.UnionWith(granted.Write);
        }

        if (requirement.Permission == Permission.Read)
        {
            values.UnionWith(granted.Read);
        }

        foreach (System.Security.Claims.Claim claim in context.User.FindAll(settings.RoleClaimType))
        {
            if (Grants(claim.Value, values))
            {
                context.Succeed(requirement);
                break;
            }
        }

        return Task.CompletedTask;
    }

    // A role claim is a string (Entra's app roles, Keycloak's), or a JSON
    // object whose property names are the roles (Zitadel's project roles).
    private static bool Grants(string value, HashSet<string> granted)
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
