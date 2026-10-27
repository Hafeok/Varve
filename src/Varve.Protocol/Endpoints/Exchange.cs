// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;

namespace Varve.Protocol.Endpoints;

/// <summary>One request to one dataset's endpoint: its context, the options, and the dataset.</summary>
internal sealed class Exchange
{
    internal const string DatasetRouteValue = "dataset";

    private Exchange(HttpContext context, ProtocolOptions options, DatasetName name, Dataset dataset, CallerScope scope)
    {
        Context = context;
        Options = options;
        Name = name;
        Dataset = dataset;
        Scope = scope;
        Authorised = options.Management?.Applied;
    }

    /// <summary>What the caller may see and change of this dataset, by graph (ADR 0107).</summary>
    internal CallerScope Scope { get; }

    /// <summary>
    /// The management position the request was authorised at, which its
    /// commit records (ADR 0165); none in a host with no management plane.
    /// </summary>
    internal Management.Model.ManagementPosition? Authorised { get; }

    internal HttpContext Context { get; }

    internal ProtocolOptions Options { get; }

    internal DatasetName Name { get; }

    internal Dataset Dataset { get; }

    internal HttpRequest Request => Context.Request;

    internal HttpResponse Response => Context.Response;

    /// <summary>
    /// Authorises the request for <paramref name="permission"/> on the dataset
    /// it names, then resolves the dataset. Writes the refusal or the
    /// <c>404</c> itself and answers <see langword="null"/>. Authorisation
    /// comes first, so that a caller who may not read cannot learn which
    /// names exist (ADRs 0091, 0093).
    /// </summary>
    internal static async Task<Exchange?> BeginAsync(HttpContext context, ProtocolOptions options, string permission)
    {
        DatasetName name = DatasetName.Default;
        bool named = context.Request.RouteValues.TryGetValue(DatasetRouteValue, out object? value);
        bool valid = !named || DatasetName.TryParse(value as string, out name);
        Describe(context, named ? value as string : null);

        if (!await AuthorizeAsync(context, options, permission, valid ? name : null).ConfigureAwait(false))
        {
            return null;
        }

        if (!valid || !options.Datasets.TryResolve(name, out Dataset? dataset))
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return null;
        }

        if (dataset.IsFollower && !await FollowAsync(context, options, name, dataset).ConfigureAwait(false))
        {
            return null;
        }

        Tracing.Dataset(context, name.Value);
        return new Exchange(context, options, name, dataset, options.AccessScopes.ScopesOf(context.User, name));
    }

    /// <summary>The dataset a request's route names, as a write's target; none when the name is not one.</summary>
    internal static WriterTarget? RouteTarget(HttpContext context) =>
        context.Request.RouteValues.TryGetValue(DatasetRouteValue, out object? value)
            ? DatasetName.TryParse(value as string, out DatasetName name) ? new WriterTarget(name) : null
            : new WriterTarget(DatasetName.Default);

    /// <summary>The read's minimum position: <c>Varve-Min-Position</c> (ADR 0168).</summary>
    internal const string MinPositionHeader = "Varve-Min-Position";

    // A follower is advanced before it answers, when it is due or the read
    // asks for a later position (ADR 0164); still behind, the read is
    // 503 dataset-behind, with no wait (ADR 0168).
    private static async Task<bool> FollowAsync(HttpContext context, ProtocolOptions options, DatasetName name, Dataset dataset)
    {
        Store.Log.Position? atLeast = null;
        StringValues header = context.Request.Headers[MinPositionHeader];

        if (!StringValues.IsNullOrEmpty(header))
        {
            if (header.Count != 1 || !long.TryParse(header[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long value))
            {
                await HttpProblems.BadRequest(context, "Varve-Min-Position is one position.").ConfigureAwait(false);
                return false;
            }

            atLeast = new Store.Log.Position(value);
        }

        if (options.Following is { } following)
        {
            await following.FollowAsync(name, dataset, atLeast, context.RequestAborted).ConfigureAwait(false);
        }

        if (atLeast is { } wanted && wanted > dataset.Head)
        {
            Preconditions.Describe(context.Response, dataset.Head);
            await HttpProblems.WriteAsync(context, ProblemType.DatasetBehind, "This instance follows the dataset and has applied position " + dataset.Head.ToString() + ".", members =>
            {
                members.Number("position", wanted.Value);
                members.Number("appliedPosition", dataset.Head.Value);
            }).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    /// <summary>
    /// <paramref name="view"/> as the caller sees it: through the readable
    /// scope, or the view itself when the caller reads every graph, so that
    /// an unscoped request costs what it did (ADR 0107).
    /// </summary>
    internal IQuadSource Readable(IQuadSource view)
    {
        // The report graphs the caller reads every bound graph of (ADR 0174).
        return Options.ValidationReports is { } reports && view is DatasetView pinned && reports.Of(Name) is { Count: > 0 } graphs
            ? ReportGraphs.Compose(pinned, Scope.Readable, graphs)
            : GraphScopedQuadSource.Wrap(view, Scope.Readable);
    }

    /// <summary>
    /// An exchange over the fleet's system dataset, for the management
    /// endpoints (ADR 0163): authorised by the endpoint, every graph in scope.
    /// </summary>
    internal static Exchange ForSystem(HttpContext context, ProtocolOptions options, Dataset system) =>
        new(context, options, DatasetName.Default, system, CallerScope.Everything);

    /// <summary>Authorises <paramref name="permission"/> on <paramref name="name"/>; writes the refusal itself.</summary>
    internal static Task<bool> AuthorizeDatasetAsync(HttpContext context, ProtocolOptions options, string permission, DatasetName name)
    {
        Describe(context, null);
        return AuthorizeAsync(context, options, permission, name);
    }

    /// <summary>Authorises this request for another permission, as an update inside a <c>POST</c> needs.</summary>
    internal Task<bool> AuthorizeAsync(string permission) => AuthorizeAsync(Context, Options, permission, Name);

    /// <summary>Authorises for a server-wide permission, decided on no dataset (ADR 0106); writes the refusal itself.</summary>
    internal static Task<bool> AuthorizeServerAsync(HttpContext context, ProtocolOptions options, string permission)
    {
        Describe(context, null);
        return AuthorizeAsync(context, options, permission, null);
    }

    /// <summary>
    /// What every dataset response carries, problems included (ADR 0119):
    /// <c>Vary</c>, because the content is per caller, per selector, per
    /// format and, for a query, per regime (ADR 0192); <c>Varve-Request-Id</c>, the id a problem's <c>instance</c>
    /// and a commit's cause repeat; and <c>Link rel="service-desc"</c> to
    /// the dataset's description when the request names a dataset.
    /// </summary>
    internal static void Describe(HttpContext context, string? datasetSegment)
    {
        HttpResponse response = context.Response;
        response.Headers.Vary = new StringValues([HeaderNames.Accept, Preconditions.AsOfHeader, HeaderNames.Authorization, EntailmentHeader.Name]);
        response.Headers[Preconditions.RequestIdHeader] = context.TraceIdentifier;

        if (datasetSegment is null)
        {
            return;
        }

        // The dataset's base: the request's path up to and including the
        // segment that names it, which is the description's address.
        string path = context.Request.Path.Value ?? "/";
        int at = path.IndexOf("/" + datasetSegment + "/", StringComparison.Ordinal);
        int end = at >= 0 ? at + datasetSegment.Length + 2 : (path.EndsWith("/" + datasetSegment, StringComparison.Ordinal) ? path.Length + 1 : -1);

        if (end < 0)
        {
            return;
        }

        string prefix = at >= 0 ? path[..end] : path + "/";
        response.Headers.Link = "<" + context.Request.PathBase.ToUriComponent() + prefix + ">; rel=\"service-desc\"";
    }

    /// <summary>Whether the caller holds <paramref name="permission"/> on <paramref name="resource"/>, writing nothing.</summary>
    internal static async Task<bool> MayAsync(HttpContext context, ProtocolOptions options, string permission, DatasetName? resource) =>
        (await options.Authorization.AuthorizeAsync(context.User, resource, permission).ConfigureAwait(false)).Succeeded;

    private static async Task<bool> AuthorizeAsync(HttpContext context, ProtocolOptions options, string permission, DatasetName? resource)
    {
        AuthorizationResult result = await options.Authorization.AuthorizeAsync(context.User, resource, permission).ConfigureAwait(false);

        if (result.Succeeded)
        {
            return true;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            await context.ForbidAsync().ConfigureAwait(false);
        }
        else
        {
            await context.ChallengeAsync().ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>The request's own address without its query: the base of every IRI a request resolves, and a directly identified graph's IRI.</summary>
    internal string Address()
    {
        HttpRequest request = Request;
        return string.Concat(request.Scheme, "://", request.Host.ToUriComponent(), request.PathBase.ToUriComponent(), request.Path.ToUriComponent());
    }
}
