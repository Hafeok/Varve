// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Store;

namespace Varve.Protocol.Endpoints;

/// <summary>One request to one dataset's endpoint: its context, the options, and the dataset.</summary>
internal sealed class Exchange
{
    internal const string DatasetRouteValue = "dataset";

    private Exchange(HttpContext context, ProtocolOptions options, DatasetName name, Dataset dataset)
    {
        Context = context;
        Options = options;
        Name = name;
        Dataset = dataset;
    }

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

        if (!await AuthorizeAsync(context, options, permission, valid ? name : null).ConfigureAwait(false))
        {
            return null;
        }

        if (!valid || !options.Datasets.TryResolve(name, out Dataset? dataset))
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return null;
        }

        return new Exchange(context, options, name, dataset);
    }

    /// <summary>Authorises this request for another permission, as an update inside a <c>POST</c> needs.</summary>
    internal Task<bool> AuthorizeAsync(string permission) => AuthorizeAsync(Context, Options, permission, Name);

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
