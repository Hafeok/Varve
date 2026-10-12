// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Varve.Protocol.Endpoints;
using Varve.Protocol.Http;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// What the host opts into of the request span (ADR 0112). The span itself
/// costs nothing until a listener subscribes to the source <c>Varve.Protocol</c>.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>The name of the <see cref="ActivitySource"/> the request span comes from.</summary>
    public const string ActivitySourceName = "Varve.Protocol";

    /// <summary>The name of the <see cref="System.Diagnostics.Metrics.Meter"/> the store measures on.</summary>
    public const string MeterName = "Varve.Store";

    /// <summary>The defaults: no query text.</summary>
    public static TelemetryOptions Default { get; } = new();

    /// <summary>
    /// Record <c>db.query.text</c>, the query or update as sent, on the span.
    /// Off by default: a query can carry data.
    /// </summary>
    public bool QueryText { get; init; }
}

/// <summary>
/// The request span (ADR 0112): one activity per request from the source
/// <c>Varve.Protocol</c>, named by the operation once it is known, with the
/// database semantic-convention attributes and <c>varve.</c>-prefixed ones
/// where they have none. Every member reads <see cref="Activity.Current"/>
/// and does nothing unless it is this source's: without a listener no
/// activity is started and the handler runs as it would.
/// </summary>
internal static class Tracing
{
    internal static readonly ActivitySource Source = new(TelemetryOptions.ActivitySourceName);

    /// <summary>
    /// <paramref name="handler"/> inside a span named <paramref name="operation"/>
    /// while a listener is attached, and with every refusal logged while the
    /// host's logger takes information; the handler alone otherwise.
    /// </summary>
    internal static RequestDelegate Traced(string operation, ProtocolOptions options, Func<HttpContext, Task> handler) =>
        context => Source.HasListeners() || options.Logger.IsEnabled(LogLevel.Information)
            ? RunAsync(operation, options, handler, context)
            : handler(context);

    /// <summary>The dataset the request names, and the selector it sent.</summary>
    internal static void Dataset(HttpContext context, string name)
    {
        if (Current() is { } activity)
        {
            activity.SetTag("db.namespace", name);

            if (context.Request.Headers.TryGetValue(Preconditions.AsOfHeader, out StringValues asOf) && !StringValues.IsNullOrEmpty(asOf))
            {
                activity.SetTag("varve.as_of", asOf.ToString());
            }
        }
    }

    /// <summary>The operation, once parsed: <c>SELECT</c>, <c>UPDATE</c>, <c>PUT graph</c>, …</summary>
    internal static void Operation(string name)
    {
        if (Current() is { } activity)
        {
            activity.DisplayName = name;
            activity.SetTag("db.operation.name", name);
        }
    }

    /// <summary>The query or update as sent, when the host opted in.</summary>
    internal static void QueryText(ProtocolOptions options, SparqlRequest request)
    {
        if (options.Telemetry.QueryText && Current() is { } activity)
        {
            activity.SetTag("db.query.text", request.Text ?? Encoding.UTF8.GetString(request.Utf8.Span));
        }
    }

    /// <summary>The position the response describes: the view's for a read, the committed one for a write.</summary>
    internal static void Position(Position position) => Current()?.SetTag("varve.position", position.Value);

    /// <summary>How many solutions a result carried, counted as they were written.</summary>
    internal static void ReturnedRows(long rows) => Current()?.SetTag("db.response.returned_rows", rows);

    // This source's activity, when the request runs inside one.
    private static Activity? Current() => Activity.Current is { } activity && ReferenceEquals(activity.Source, Source) ? activity : null;

    private static async Task RunAsync(string operation, ProtocolOptions options, Func<HttpContext, Task> handler, HttpContext context)
    {
        using Activity? activity = Source.StartActivity(operation, ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag("db.system.name", "varve");
            activity.SetTag("db.operation.name", operation);
            activity.SetTag("varve.request_id", context.TraceIdentifier);
        }

        try
        {
            await handler(context).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            throw;
        }

        int status = context.Response.StatusCode;

        if (activity is not null)
        {
            activity.SetTag("http.response.status_code", status);

            if (status >= StatusCodes.Status500InternalServerError)
            {
                activity.SetStatus(ActivityStatusCode.Error);
            }
        }

        if (status >= StatusCodes.Status400BadRequest)
        {
            ProtocolLog.Refused(options.Logger, context.Request.Method, context.Request.Path.Value ?? "/", status, context.TraceIdentifier);
        }
    }
}
