// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Varve.Protocol.Http;
using Varve.Protocol.Model;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// Write routing by forwarding (ADR 0168): a write to a dataset this instance
/// does not write is sent, unchanged, to the instance that does, and its
/// answer is passed back unchanged. The forwarding instance authenticates the
/// caller first and authorises nothing: the writer authenticates again,
/// authorises at its own management position, and names the caller in the
/// commit. A forwarded request carries the hop header, and an instance that
/// receives one and does not write the dataset answers <c>writer-moved</c>
/// and never forwards it again.
/// </summary>
internal static class Forwarding
{
    /// <summary>The hop header: the forwarding instance's name.</summary>
    internal const string HopHeader = "Varve-Forwarded";

    // What a write's meaning depends on, passed as it came. Content headers
    // travel with the body.
    private static readonly ImmutableArray<string> Passed =
    [
        "Authorization", "Accept", "If-Match", "If-None-Match", Preconditions.IfGraphsMatchHeader, "Varve-Min-Management-Position", "traceparent", "tracestate",
    ];

    // What the forwarder's own connection decides, never passed back.
    private static readonly ImmutableArray<string> HopByHop = ["Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "Proxy-Connection"];

    /// <summary>
    /// Sends the write to its writer when this instance does not write
    /// <paramref name="target"/>. True when the request was answered here:
    /// forwarded, refused by the hop rule, or refused for want of a caller or
    /// a writer. False when it is this instance's to serve. The body, when
    /// the caller already read it, is <paramref name="body"/>; otherwise the
    /// request's own body is read whole first.
    /// </summary>
    internal static async Task<bool> RouteAsync(HttpContext context, ProtocolOptions options, WriterTarget target, byte[]? body = null)
    {
        if (options.Writers is not { } directory)
        {
            return false;
        }

        WriterRoute route = await directory.RouteAsync(target, context.RequestAborted).ConfigureAwait(false);

        if (route.Kind == WriterRouteKind.Local)
        {
            return false;
        }

        Exchange.Describe(context, null);

        if (!StringValues.IsNullOrEmpty(context.Request.Headers[HopHeader]))
        {
            // Forwarded once already, to an instance that is no longer the
            // writer: the client retries, which reads the route again.
            directory.Stale(target);
            await HttpProblems.WriteAsync(context, ProblemType.WriterMoved, "This instance does not write the dataset; a forwarded write is never forwarded again (ADR 0168).", members =>
            {
                members.Number("epoch", route.Epoch);
                members.String("requestId", context.TraceIdentifier);
            }).ConfigureAwait(false);
            return true;
        }

        // The edge authenticates: an anonymous request is refused here, before
        // any byte reaches the writer. A host in anonymous mode allows it.
        if (context.User.Identity?.IsAuthenticated != true && !await Exchange.MayAsync(context, options, DatasetPermissions.Read, null).ConfigureAwait(false))
        {
            await context.ChallengeAsync().ConfigureAwait(false);
            return true;
        }

        if (route.Kind == WriterRouteKind.NoWriter || route.Address is null)
        {
            await HttpProblems.WriteAsync(context, ProblemType.Unavailable, "No instance holds the writer epoch now; a writer that starts claims it.",
                members => members.String("reason", "no-writer")).ConfigureAwait(false);
            return true;
        }

        if (body is null)
        {
            Body read = await RequestBodies.ReadAsync(context, options.Limits).ConfigureAwait(false);

            if (read.TooLarge)
            {
                await HttpProblems.RequestTooLarge(context, options.Limits.MaxRequestBody.Value).ConfigureAwait(false);
                return true;
            }

            body = read.Bytes ?? [];
        }

        await ForwardAsync(context, options, directory, target, route.Address, body).ConfigureAwait(false);
        return true;
    }

    private static async Task ForwardAsync(HttpContext context, ProtocolOptions options, IWriterDirectory directory, WriterTarget target, string address, byte[] body)
    {
        HttpRequest request = context.Request;
        string url = address.TrimEnd('/') + request.PathBase.ToUriComponent() + request.Path.ToUriComponent() + request.QueryString.ToUriComponent();
        using HttpRequestMessage message = new(new HttpMethod(request.Method), url);

        foreach (string name in Passed)
        {
            if (request.Headers.TryGetValue(name, out StringValues values) && !StringValues.IsNullOrEmpty(values))
            {
                foreach (string? value in values)
                {
                    message.Headers.TryAddWithoutValidation(name, value ?? string.Empty);
                }
            }
        }

        message.Headers.TryAddWithoutValidation(HopHeader, directory.Instance.Value);

        if (body.Length > 0 || !HttpMethods.IsGet(request.Method))
        {
            message.Content = new ByteArrayContent(body);

            if (request.ContentType is { } type)
            {
                message.Content.Headers.TryAddWithoutValidation("Content-Type", type);
            }
        }

        HttpResponseMessage response;

        try
        {
            response = await (options.Forwarder ?? throw new InvalidOperationException("ProtocolOptions.Forwarder is required with Writers (ADR 0168)."))
                .SendAsync(message, context.RequestAborted).ConfigureAwait(false);
        }
        catch (HttpRequestException unreachable)
        {
            // Sent once, never again: whether the writer received any of it is
            // unknown to this instance, so the client decides (ADR 0168).
            directory.Stale(target);
            await HttpProblems.WriteAsync(context, ProblemType.Unavailable, "The writer at " + address + " could not be reached: " + unreachable.Message,
                members => members.String("reason", "writer-unreachable")).ConfigureAwait(false);
            return;
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                directory.Stale(target);
            }

            HttpResponse answer = context.Response;
            answer.StatusCode = (int)response.StatusCode;
            answer.Headers.Remove("Varve-Request-Id");
            answer.Headers.Remove("Vary");
            answer.Headers.Remove("Link");

            foreach ((string name, System.Collections.Generic.IEnumerable<string> values) in response.Headers)
            {
                if (!HopByHop.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    answer.Headers[name] = new StringValues([.. values]);
                }
            }

            foreach ((string name, System.Collections.Generic.IEnumerable<string> values) in response.Content.Headers)
            {
                answer.Headers[name] = new StringValues([.. values]);
            }

            await response.Content.CopyToAsync(answer.Body, context.RequestAborted).ConfigureAwait(false);
        }
    }
}
