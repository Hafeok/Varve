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
/// RFC 9457 problem details on every error response (ADR 0092), written with
/// <see cref="Utf8JsonWriter"/>: no reflection, nothing for trimming to keep.
/// </summary>
internal static class HttpProblems
{
    internal const string MediaType = "application/problem+json";

    /// <summary>Writes a problem as the whole response.</summary>
    internal static async Task WriteAsync(
        HttpContext context,
        int status,
        ProblemType type,
        string title,
        string? detail = null,
        Action<Utf8JsonWriter>? extensions = null)
    {
        HttpResponse response = context.Response;
        response.StatusCode = status;
        response.ContentType = MediaType;
        ArrayBufferWriter<byte> body = new(512);

        using (Utf8JsonWriter json = new(body))
        {
            json.WriteStartObject();
            json.WriteString("type", type.Value);
            json.WriteString("title", title);
            json.WriteNumber("status", status);

            if (detail is not null)
            {
                json.WriteString("detail", detail);
            }

            extensions?.Invoke(json);
            json.WriteEndObject();
        }

        response.ContentLength = body.WrittenCount;
        await response.Body.WriteAsync(body.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }

    internal static Task BadRequest(HttpContext context, string detail) =>
        WriteAsync(context, StatusCodes.Status400BadRequest, ProblemType.BadRequest, "The request is not one the protocol accepts.", detail);

    internal static Task UnsupportedMediaType(HttpContext context, string detail) =>
        WriteAsync(context, StatusCodes.Status415UnsupportedMediaType, ProblemType.UnsupportedMediaType, "The request body is in a media type or charset this endpoint does not read.", detail);

    internal static Task NotAcceptable(HttpContext context, string detail) =>
        WriteAsync(context, StatusCodes.Status406NotAcceptable, ProblemType.NotAcceptable, "Nothing in Accept is a format this endpoint writes.", detail);

    internal static Task MethodNotAllowed(HttpContext context, string allow)
    {
        context.Response.Headers.Allow = allow;
        return WriteAsync(context, StatusCodes.Status405MethodNotAllowed, ProblemType.MethodNotAllowed, "The endpoint does not serve this method.", "Allowed: " + allow + ".");
    }

    internal static Task DatasetNotFound(HttpContext context) =>
        WriteAsync(context, StatusCodes.Status404NotFound, ProblemType.DatasetNotFound, "No dataset by that name.");

    internal static Task RequestTooLarge(HttpContext context) =>
        WriteAsync(context, StatusCodes.Status413PayloadTooLarge, ProblemType.RequestTooLarge, "The request body is over the server's limit.");
}
