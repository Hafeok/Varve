// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Varve.Protocol.Endpoints;

namespace Varve.Protocol;

/// <summary>
/// Maps one dataset's endpoints under the route builder it is given (ADRs
/// 0091, 0093, 0118): <c>/</c>, <c>/sparql</c>, <c>/graphs</c>, <c>/commits</c>,
/// <c>/diff</c>, <c>/status</c>, <c>/settings</c> and <c>/checkpoints</c>.
/// </summary>
/// <remarks>
/// The dataset is the route's <c>dataset</c> value when the host's prefix has
/// one (<c>/datasets/{dataset}</c>), and <see cref="Model.DatasetName.Default"/>
/// when it has none. Every endpoint authorises first, through
/// <see cref="ProtocolOptions.Authorization"/>, by the names in
/// <see cref="DatasetPermissions"/>. Handlers are request delegates, so
/// nothing is bound by reflection and the endpoints run under Native AOT.
/// </remarks>
public static class VarveEndpoints
{
    /// <summary>
    /// Maps every endpoint of a dataset under fixed names: <c>/</c>,
    /// <c>/sparql</c>, <c>/graphs</c>, <c>/commits</c>, <c>/diff</c>, <c>/status</c>,
    /// <c>/settings</c> and <c>/checkpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapVarveDataset(this IEndpointRouteBuilder routes, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        routes.MapServiceDescription("/", options);
        routes.MapSparqlEndpoint("/sparql", options);
        routes.MapGraphStore("/graphs", options);
        routes.MapCommits("/commits", options);
        routes.MapDiff("/diff", options);
        routes.MapDatasetStatus("/status", options);
        routes.Map("/settings", Tracing.Traced("settings", options, context => AdminEndpoints.SettingsAsync(context, options)));
        routes.Map("/checkpoints", Tracing.Traced("checkpoints", options, context => AdminEndpoints.CheckpointAsync(context, options)));
        return routes;
    }

    /// <summary>
    /// The admin API over the host's datasets (ADRs 0106, 0118), under
    /// <paramref name="pattern"/>, the prefix the dataset groups are mounted
    /// under (<c>/datasets</c>): <c>GET</c> lists; <c>PUT</c> and <c>DELETE</c>
    /// of <c>{pattern}/{dataset}</c> create and delete; <c>GET</c> and
    /// <c>PUT</c> of <c>{pattern}/{dataset}/state</c> read and set open or
    /// closed. The dataset groups' own routes keep <c>GET</c> of
    /// <c>{pattern}/{dataset}</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapVarveAdministration(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(options);
        string prefix = pattern.TrimEnd('/');
        routes.MapMethods(prefix, ["GET"], Tracing.Traced("datasets", options, context => AdminEndpoints.ListAsync(context, options)));
        routes.MapMethods(prefix + "/{" + Exchange.DatasetRouteValue + "}", ["PUT"], Tracing.Traced("create dataset", options, context => AdminEndpoints.CreateAsync(context, options)));
        routes.MapMethods(prefix + "/{" + Exchange.DatasetRouteValue + "}", ["DELETE"], Tracing.Traced("delete dataset", options, context => AdminEndpoints.DeleteAsync(context, options)));
        routes.Map(prefix + "/{" + Exchange.DatasetRouteValue + "}/state", Tracing.Traced("state", options, context => AdminEndpoints.StateAsync(context, options)));
        return routes;
    }

    /// <summary>The SPARQL 1.1 Protocol's query and update operations on one endpoint, and its service description.</summary>
    public static IEndpointConventionBuilder MapSparqlEndpoint(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        return routes.Map(pattern, Tracing.Traced("sparql", options, context => SparqlEndpoint.HandleAsync(context, options)));
    }

    /// <summary>
    /// The Graph Store: <paramref name="pattern"/> for indirect identification
    /// and the store itself, and everything under it for direct identification.
    /// </summary>
    public static IEndpointRouteBuilder MapGraphStore(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(options);
        routes.Map(pattern, Tracing.Traced("graph", options, context => GraphStoreEndpoint.HandleAsync(context, options)));
        routes.Map(pattern.TrimEnd('/') + "/{**" + GraphStoreEndpoint.PathValue + "}", Tracing.Traced("graph", options, context => GraphStoreEndpoint.HandleAsync(context, options)));
        return routes;
    }

    /// <summary>The service description, for <c>GET</c> with an RDF <c>Accept</c>.</summary>
    public static IEndpointConventionBuilder MapServiceDescription(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        return routes.Map(pattern, Tracing.Traced("service description", options, context => ServiceDescriptionEndpoint.HandleAsync(context, options)));
    }

    /// <summary>
    /// The commits resource (ADRs 0097, 0118): <paramref name="pattern"/> for
    /// a range or a live tail, and <c>{pattern}/{position}</c> for one commit.
    /// </summary>
    public static IEndpointRouteBuilder MapCommits(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(options);
        routes.Map(pattern, Tracing.Traced("commits", options, context => CommitsEndpoint.HandleAsync(context, options)));
        routes.Map(pattern.TrimEnd('/') + "/{" + CommitsEndpoint.PositionRouteValue + "}", Tracing.Traced("commit", options, context => CommitsEndpoint.HandleOneAsync(context, options)));
        return routes;
    }

    /// <summary>The diff between two positions (ADR 0097).</summary>
    public static IEndpointConventionBuilder MapDiff(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        return routes.Map(pattern, Tracing.Traced("diff", options, context => DiffEndpoint.HandleAsync(context, options)));
    }

    /// <summary>The dataset's status, for <c>admin</c> (ADR 0101).</summary>
    public static IEndpointConventionBuilder MapDatasetStatus(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        return routes.Map(pattern, Tracing.Traced("status", options, context => StatusEndpoint.HandleAsync(context, options)));
    }
}
