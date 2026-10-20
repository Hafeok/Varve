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
/// <c>/diff</c> and <c>/events</c>. A dataset's status, settings and
/// checkpoints are the management plane's (ADR 0163), mapped by
/// <see cref="MapVarveManagement"/>.
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
    /// Maps every data-plane endpoint of a dataset under fixed names:
    /// <c>/</c>, <c>/sparql</c>, <c>/graphs</c>, <c>/commits</c>, <c>/diff</c>
    /// and <c>/events</c>.
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
        routes.MapEvents("/events", options);
        return routes;
    }

    /// <summary>
    /// A dataset's events (ADRs 0143–0145): <paramref name="pattern"/> for a
    /// range of rendered CloudEvents, <c>{pattern}/types</c> for the types,
    /// <c>{pattern}/types/{type}</c>, <c>…/versions/{n}</c> and
    /// <c>…/revisions/{r}</c> for a schema, and <c>{pattern}/asyncapi</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapEvents(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(options);
        string prefix = pattern.TrimEnd('/');
        string type = prefix + "/types/{" + EventsEndpoints.TypeRouteValue + "}";
        routes.Map(prefix, Tracing.Traced("events", options, context => EventsEndpoints.HandleAsync(context, options)));
        routes.Map(prefix + "/types", Tracing.Traced("event types", options, context => EventsEndpoints.TypesAsync(context, options)));
        routes.Map(type, Tracing.Traced("event schema", options, context => EventsEndpoints.SchemaAsync(context, options)));
        routes.Map(type + "/versions/{" + EventsEndpoints.VersionRouteValue + "}", Tracing.Traced("event schema", options, context => EventsEndpoints.SchemaAsync(context, options)));
        routes.Map(type + "/versions/{" + EventsEndpoints.VersionRouteValue + "}/revisions/{" + EventsEndpoints.RevisionRouteValue + "}",
            Tracing.Traced("event schema", options, context => EventsEndpoints.SchemaAsync(context, options)));
        routes.Map(prefix + "/asyncapi", Tracing.Traced("asyncapi", options, context => EventsEndpoints.AsyncApiAsync(context, options)));
        return routes;
    }

    /// <summary>
    /// The server-wide list of event types (ADR 0144): every dataset's types
    /// the caller reads, each with its dataset, under <paramref name="pattern"/>
    /// (<c>/events/types</c>). Needs <see cref="ProtocolOptions.Administration"/>
    /// to list the datasets.
    /// </summary>
    public static IEndpointConventionBuilder MapVarveEventTypes(this IEndpointRouteBuilder routes, string pattern, ProtocolOptions options)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        return routes.Map(pattern, Tracing.Traced("server event types", options, context => EventsEndpoints.ServerTypesAsync(context, options)));
    }

    /// <summary>
    /// The management plane's resources (ADRs 0163, 0168), at <c>/manage</c>:
    /// <c>datasets</c>, and under <c>datasets/{dataset}</c> the dataset's
    /// declaration, <c>settings</c>, <c>state</c>, <c>status</c>,
    /// <c>checkpoints</c> and <c>grants</c>; then <c>fleet</c>, <c>graphs</c>,
    /// <c>apply</c> and <c>system</c>. A host with no management plane passes
    /// none: it maps the list, each dataset's status, checkpoints, settings and
    /// state, which then act on the datasets it holds, and nothing it would
    /// need a system dataset for.
    /// </summary>
    public static IEndpointRouteBuilder MapVarveManagement(this IEndpointRouteBuilder routes, ProtocolOptions options, IManagementPlane? plane)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        const string Datasets = "/manage/datasets";
        const string Dataset = Datasets + "/{" + Exchange.DatasetRouteValue + "}";
        routes.MapMethods(Datasets, ["GET"], Tracing.Traced("datasets", options, context => ManagementEndpoints.ListAsync(context, options)));
        routes.Map(Dataset + "/status", Tracing.Traced("status", options, context => StatusEndpoint.HandleAsync(context, options)));
        routes.Map(Dataset + "/checkpoints", Tracing.Traced("checkpoints", options, context => ManagementEndpoints.CheckpointAsync(context, options)));

        if (plane is null)
        {
            routes.Map(Dataset + "/settings", Tracing.Traced("settings", options, context => AdminEndpoints.SettingsAsync(context, options)));
            routes.Map(Dataset + "/state", Tracing.Traced("state", options, context => AdminEndpoints.StateAsync(context, options)));
            return routes;
        }

        routes.Map(Dataset, Tracing.Traced("dataset declaration", options, context => ManagementEndpoints.DatasetAsync(context, options, plane)));
        routes.Map(Dataset + "/settings", Tracing.Traced("settings", options, context => ManagementEndpoints.SettingsAsync(context, options, plane)));
        routes.Map(Dataset + "/state", Tracing.Traced("state", options, context => ManagementEndpoints.StateAsync(context, options, plane)));
        routes.Map(Dataset + "/grants", Tracing.Traced("grants", options, context => ManagementEndpoints.GrantsAsync(context, options, plane)));
        routes.Map("/manage/fleet", Tracing.Traced("fleet declaration", options, context => ManagementEndpoints.FleetAsync(context, options, plane)));
        routes.Map("/manage/graphs", Tracing.Traced("management graphs", options, context => ManagementEndpoints.GraphsAsync(context, options, plane)));
        routes.Map("/manage/apply", Tracing.Traced("apply", options, context => ManagementEndpoints.ApplyAsync(context, options, plane)));
        routes.Map("/manage/system", Tracing.Traced("system", options, context => ManagementEndpoints.SystemAsync(context, options, plane)));
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
}
