// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Protocol.Model;

namespace Varve.Protocol.Http;

/// <summary>
/// The extension members of one problem, written through the catalogue: a
/// name the type does not declare is a defect, thrown rather than written
/// (ADR 0119).
/// </summary>
internal readonly struct ProblemMembers
{
    private readonly Utf8JsonWriter _json;
    private readonly ProblemShape _shape;

    internal ProblemMembers(Utf8JsonWriter json, ProblemShape shape)
    {
        _json = json;
        _shape = shape;
    }

    internal void Number(string name, long value)
    {
        Check(name);
        _json.WriteNumber(name, value);
    }

    internal void String(string name, string value)
    {
        Check(name);
        _json.WriteString(name, value);
    }

    /// <summary>An array or object member, written by the caller between its start and end.</summary>
    internal Utf8JsonWriter Raw(string name)
    {
        Check(name);
        return _json;
    }

    private void Check(string name)
    {
        if (!_shape.Allows(name))
        {
            throw new InvalidOperationException("The problem type " + _shape.Name + " declares no member '" + name + "' (ADR 0119).");
        }
    }
}

/// <summary>
/// RFC 9457 problem details on every error response (ADRs 0092, 0119), the
/// status and the title from the catalogue, <c>instance</c> the request id,
/// written with <see cref="Utf8JsonWriter"/>: no reflection, nothing for
/// trimming to keep.
/// </summary>
internal static class HttpProblems
{
    internal const string MediaType = "application/problem+json";

    /// <summary>Writes a problem of <paramref name="type"/> as the whole response.</summary>
    internal static async Task WriteAsync(
        HttpContext context,
        ProblemType type,
        string? detail = null,
        Action<ProblemMembers>? members = null)
    {
        ProblemShape shape = ProblemCatalogue.Of(type);
        HttpResponse response = context.Response;
        response.StatusCode = shape.Status;
        response.ContentType = MediaType;
        response.Headers[Preconditions.RequestIdHeader] = context.TraceIdentifier;

        // Every dataset response varies by these, problems included (ADR
        // 0119), and a problem written before the exchange began — a 405 on
        // the method alone — has not been described yet.
        if (response.Headers.Vary.Count == 0)
        {
            response.Headers.Vary = new Microsoft.Extensions.Primitives.StringValues([Microsoft.Net.Http.Headers.HeaderNames.Accept, Preconditions.AsOfHeader, Microsoft.Net.Http.Headers.HeaderNames.Authorization]);
        }
        ArrayBufferWriter<byte> body = new(512);

        using (Utf8JsonWriter json = new(body))
        {
            json.WriteStartObject();
            json.WriteString("type", type.Value);
            json.WriteString("title", shape.Title);
            json.WriteNumber("status", shape.Status);

            if (detail is not null)
            {
                json.WriteString("detail", detail);
            }

            json.WriteString("instance", context.TraceIdentifier);
            members?.Invoke(new ProblemMembers(json, shape));
            json.WriteEndObject();
        }

        response.ContentLength = body.WrittenCount;
        await response.Body.WriteAsync(body.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }

    internal static Task BadRequest(HttpContext context, string detail) => WriteAsync(context, ProblemType.BadRequest, detail);

    internal static Task UnsupportedMediaType(HttpContext context, string detail) => WriteAsync(context, ProblemType.UnsupportedMediaType, detail);

    internal static Task NotAcceptable(HttpContext context, string detail) => WriteAsync(context, ProblemType.NotAcceptable, detail);

    internal static Task MethodNotAllowed(HttpContext context, string allow)
    {
        context.Response.Headers.Allow = allow;
        return WriteAsync(context, ProblemType.MethodNotAllowed, "Allowed: " + allow + ".");
    }

    internal static Task DatasetNotFound(HttpContext context) => WriteAsync(context, ProblemType.DatasetNotFound);

    internal static Task RequestTooLarge(HttpContext context, long limit) =>
        WriteAsync(context, ProblemType.RequestTooLarge, null, members => members.Number("limit", limit));
}
