// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Varve.Protocol.Http;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// <c>/sparql</c>: the SPARQL 1.1 Protocol's query and update operations on
/// one endpoint (ADR 0092), and the service description for a <c>GET</c> with
/// neither.
/// </summary>
internal static class SparqlEndpoint
{
    internal static async Task HandleAsync(HttpContext context, ProtocolOptions options)
    {
        string method = context.Request.Method;

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
        {
            await GetAsync(context, options).ConfigureAwait(false);
        }
        else if (HttpMethods.IsPost(method))
        {
            await PostAsync(context, options).ConfigureAwait(false);
        }
        else
        {
            await HttpProblems.MethodNotAllowed(context, "GET, HEAD, POST").ConfigureAwait(false);
        }
    }

    private static async Task GetAsync(HttpContext context, ProtocolOptions options)
    {
        if (await Exchange.BeginAsync(context, options, DatasetPermissions.Read).ConfigureAwait(false) is not { } exchange)
        {
            return;
        }

        IQueryCollection query = context.Request.Query;
        StringValues text = query["query"];

        if (StringValues.IsNullOrEmpty(text))
        {
            if (!StringValues.IsNullOrEmpty(query["update"]))
            {
                // §2.2: an update is a POST.
                await HttpProblems.MethodNotAllowed(context, "POST").ConfigureAwait(false);
                return;
            }

            await ServiceDescriptionEndpoint.WriteAsync(exchange).ConfigureAwait(false);
            return;
        }

        if (text.Count > 1)
        {
            await HttpProblems.BadRequest(context, "A request carries exactly one query.").ConfigureAwait(false);
            return;
        }

        await QueryRun.RunAsync(exchange, new SparqlRequest
        {
            Text = text[0],
            DefaultGraphs = query["default-graph-uri"],
            NamedGraphs = query["named-graph-uri"],
            Version = Single(query["version"]),
        }).ConfigureAwait(false);
    }

    private static async Task PostAsync(HttpContext context, ProtocolOptions options)
    {
        // Read is the least any POST needs; an update asks for write too.
        if (await Exchange.BeginAsync(context, options, DatasetPermissions.Read).ConfigureAwait(false) is not { } exchange)
        {
            return;
        }

        string? contentType = context.Request.ContentType;

        if (!MediaTypes.TryReadContentType(contentType, out string mediaType, out bool utf8))
        {
            await HttpProblems.BadRequest(context, "A POST carries a Content-Type: application/x-www-form-urlencoded, application/sparql-query or application/sparql-update.").ConfigureAwait(false);
            return;
        }

        if (!utf8)
        {
            await HttpProblems.UnsupportedMediaType(context, "Every request body is UTF-8.").ConfigureAwait(false);
            return;
        }

        if (mediaType is not (MediaTypes.Form or MediaTypes.SparqlQuery or MediaTypes.SparqlUpdate))
        {
            await HttpProblems.UnsupportedMediaType(context, "The endpoint reads application/x-www-form-urlencoded, application/sparql-query and application/sparql-update.").ConfigureAwait(false);
            return;
        }

        Body body = await RequestBodies.ReadAsync(context, options.Limits).ConfigureAwait(false);

        if (body.TooLarge)
        {
            await HttpProblems.RequestTooLarge(context).ConfigureAwait(false);
            return;
        }

        IQueryCollection parameters = context.Request.Query;

        switch (mediaType)
        {
            case MediaTypes.SparqlQuery:
                await QueryRun.RunAsync(exchange, new SparqlRequest
                {
                    Utf8 = body.Bytes,
                    DefaultGraphs = parameters["default-graph-uri"],
                    NamedGraphs = parameters["named-graph-uri"],
                    Version = MediaTypes.VersionParameter(contentType) ?? Single(parameters["version"]),
                }).ConfigureAwait(false);
                return;

            case MediaTypes.SparqlUpdate:
                await UpdateRun.RunAsync(exchange, new SparqlRequest
                {
                    Utf8 = body.Bytes,
                    DefaultGraphs = parameters["using-graph-uri"],
                    NamedGraphs = parameters["using-named-graph-uri"],
                    Version = MediaTypes.VersionParameter(contentType) ?? Single(parameters["version"]),
                }).ConfigureAwait(false);
                return;
        }

        Dictionary<string, StringValues> form = RequestBodies.ReadForm(body.Bytes!);
        StringValues queryText = Field(form, "query");
        StringValues updateText = Field(form, "update");

        if (queryText.Count + updateText.Count != 1)
        {
            await HttpProblems.BadRequest(context, "A form carries exactly one query or exactly one update.").ConfigureAwait(false);
            return;
        }

        if (queryText.Count == 1)
        {
            await QueryRun.RunAsync(exchange, new SparqlRequest
            {
                Text = queryText[0],
                DefaultGraphs = Field(form, "default-graph-uri"),
                NamedGraphs = Field(form, "named-graph-uri"),
                Version = Single(Field(form, "version")),
            }).ConfigureAwait(false);
            return;
        }

        await UpdateRun.RunAsync(exchange, new SparqlRequest
        {
            Text = updateText[0],
            DefaultGraphs = Field(form, "using-graph-uri"),
            NamedGraphs = Field(form, "using-named-graph-uri"),
            Version = Single(Field(form, "version")),
        }).ConfigureAwait(false);
    }

    private static StringValues Field(Dictionary<string, StringValues> form, string name) =>
        form.TryGetValue(name, out StringValues values) ? values : StringValues.Empty;

    // A repeated version is a version nobody can choose between: no value,
    // which the parse step refuses as unknown.
    private static string? Single(StringValues values) => values.Count switch
    {
        0 => null,
        1 => values[0],
        _ => string.Empty,
    };
}
