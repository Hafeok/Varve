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
/// 0091, 0093): <c>/</c>, <c>/sparql</c>, <c>/graphs</c>, <c>/feed</c>,
/// <c>/diff</c> and <c>/status</c>.
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
    /// <c>/sparql</c>, <c>/graphs</c>, <c>/feed</c>, <c>/diff</c> and <c>/status</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapVarveDataset(this IEndpointRouteBuilder routes, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        routes.MapServiceDescription("/", options);
        routes.MapSparqlEndpoint("/sparql", options);
        routes.MapGraphStore("/graphs", options);
        routes.MapChangeFeed("/feed", options);
        routes.MapDiff("/diff", options);
        routes.MapDatasetStatus("/status", options);
        return routes;
    }

    /// <summary>The SPARQL 1.1 Protocol's query and update operations on one endpoint, and its service description.</summary>
    public static IEndpointConventionBuilder MapSparqlEndpoint(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        return routes.Map(pattern, context => SparqlEndpoint.HandleAsync(context, options));
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
        routes.Map(pattern, context => GraphStoreEndpoint.HandleAsync(context, options));
        routes.Map(pattern.TrimEnd('/') + "/{**" + GraphStoreEndpoint.PathValue + "}", context => GraphStoreEndpoint.HandleAsync(context, options));
        return routes;
    }

    /// <summary>The service description, for <c>GET</c> with an RDF <c>Accept</c>.</summary>
    public static IEndpointConventionBuilder MapServiceDescription(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        return routes.Map(pattern, context => ServiceDescriptionEndpoint.HandleAsync(context, options));
    }

    /// <summary>The change feed (ADR 0097).</summary>
    public static IEndpointConventionBuilder MapChangeFeed(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        return routes.Map(pattern, context => FeedEndpoint.HandleAsync(context, options));
    }

    /// <summary>The diff between two positions (ADR 0097).</summary>
    public static IEndpointConventionBuilder MapDiff(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        return routes.Map(pattern, context => DiffEndpoint.HandleAsync(context, options));
    }

    /// <summary>The dataset's status, for <c>admin</c> (ADR 0101).</summary>
    public static IEndpointConventionBuilder MapDatasetStatus(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        return routes.Map(pattern, context => StatusEndpoint.HandleAsync(context, options));
    }
}
