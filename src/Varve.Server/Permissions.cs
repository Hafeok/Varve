// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Varve.Events;
using Varve.Management;
using Varve.Management.Model;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Rdf;

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
/// Where the server's grants come from: the fleet, once it has started. The
/// authorisation services are built before it, and ask through this.
/// </summary>
internal sealed class FleetHandle
{
    internal Fleet? Fleet { get; set; }

    internal GrantTable Table => Fleet?.Table ?? throw new InvalidOperationException("The fleet has not started.");
}

/// <summary>
/// Decides a <see cref="DatasetPermission"/> (ADRs 0163, 0166) from the grants
/// pinned for the request: <c>read</c> when any grant on the dataset names the
/// caller, <c>write</c> when a write grant does, <c>admin</c> when an admin
/// grant does, and the server-admin permission, the operator's, from the
/// fleet graph and the bootstrap operators. An operator holds every
/// permission on every dataset.
/// </summary>
internal sealed class DatasetPermissionHandler(FleetHandle fleet, AuthSettings settings, IHttpContextAccessor accessor) : AuthorizationHandler<DatasetPermission>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DatasetPermission requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        GrantTable table = fleet.Table;
        Caller caller = Callers.Of(context.User, settings, accessor.HttpContext);
        string? dataset = (context.Resource as DatasetName?)?.Value;
        bool granted = requirement.Permission switch
        {
            Permission.ServerAdmin => table.IsOperator(caller),
            _ when dataset is null => false,
            Permission.Admin => table.IsAdmin(caller, dataset),
            Permission.Write => table.Writes(caller, dataset),
            _ => table.Reaches(caller, dataset),
        };

        if (granted)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// The host's <see cref="IAccessScopes"/> (ADRs 0107, 0166): the caller's
/// readable and writable graphs from the grants pinned for the request,
/// templates expanded; an event catalogue graph governed by its source graph
/// (ADRs 0130, 0144).
/// </summary>
internal sealed class ManagedScopes(FleetHandle fleet, AuthSettings settings, IHttpContextAccessor accessor) : IAccessScopes
{
    public CallerScope ScopesOf(ClaimsPrincipal caller, DatasetName dataset)
    {
        CallerScope scope = fleet.Table.ScopesOf(Callers.Of(caller, settings, accessor.HttpContext), dataset.Value);

        return scope.IsAdmin
            ? scope
            : new CallerScope(scope.Readable.WithGovernor(EventCatalogue.TryGetSourceGraph), scope.Writable.WithGovernor(EventCatalogue.TryGetSourceGraph), AdminAccess.None);
    }
}

/// <summary>The management model's caller from a validated token, built once per request.</summary>
internal static class Callers
{
    private const string Key = "Varve.Server.Caller";

    internal static Caller Of(ClaimsPrincipal user, AuthSettings settings, HttpContext? context)
    {
        if (context?.Items[Key] is Caller cached && ReferenceEquals(context.User, user))
        {
            return cached;
        }

        Dictionary<string, List<string>> claims = new(StringComparer.Ordinal);

        foreach (Claim claim in user.Claims)
        {
            if (!claims.TryGetValue(claim.Type, out List<string>? values))
            {
                values = [];
                claims[claim.Type] = values;
            }

            if (claim.Type == settings.RoleClaimType && claim.Value.StartsWith('{'))
            {
                // A role claim is a string (Entra's app roles, Keycloak's), or
                // a JSON object whose property names are the roles (Zitadel's).
                values.AddRange(RolesOf(claim.Value));
            }
            else
            {
                values.Add(claim.Value);
            }
        }

        Dictionary<string, IReadOnlyList<string>> held = new(StringComparer.Ordinal);

        foreach ((string type, List<string> values) in claims)
        {
            held[type] = values;
        }

        string? issuer = user.FindFirst("iss")?.Value;
        string? subject = user.FindFirst(settings.SubjectClaim)?.Value;
        string? agent = string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(subject) || issuer.Contains('#') ? null : issuer + "#" + TokenIdentity.Encode(subject);
        Caller caller = new(agent, subject, held, settings.RoleClaimType);

        if (context is not null && ReferenceEquals(context.User, user))
        {
            context.Items[Key] = caller;
        }

        return caller;
    }

    private static List<string> RolesOf(string value)
    {
        List<string> roles = [];

        try
        {
            using JsonDocument document = JsonDocument.Parse(value);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty role in document.RootElement.EnumerateObject())
                {
                    roles.Add(role.Name);
                }
            }
        }
        catch (JsonException)
        {
            roles.Add(value);
        }

        return roles;
    }
}
