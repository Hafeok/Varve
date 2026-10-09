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
    }

    /// <summary>What the caller may see and change of this dataset, by graph (ADR 0107).</summary>
    internal CallerScope Scope { get; }

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

        return new Exchange(context, options, name, dataset, options.AccessScopes.ScopesOf(context.User, name));
    }

    /// <summary>
    /// <paramref name="view"/> as the caller sees it: through the readable
    /// scope, or the view itself when the caller reads every graph, so that
    /// an unscoped request costs what it did (ADR 0107).
    /// </summary>
    internal IQuadSource Readable(IQuadSource view) => GraphScopedQuadSource.Wrap(view, Scope.Readable);

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
    /// <c>Vary</c>, because the content is per caller, per selector and per
    /// format; <c>Varve-Request-Id</c>, the id a problem's <c>instance</c>
    /// and a commit's cause repeat; and <c>Link rel="service-desc"</c> to
    /// the dataset's description when the request names a dataset.
    /// </summary>
    internal static void Describe(HttpContext context, string? datasetSegment)
    {
        HttpResponse response = context.Response;
        response.Headers.Vary = new StringValues([HeaderNames.Accept, Preconditions.AsOfHeader, HeaderNames.Authorization]);
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
