// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Protocol.Http;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// <c>GET /status</c>, the one <c>admin</c> endpoint of milestone 7a (ADR
/// 0101): the dataset's identity, head, durability, settings, checkpoints and
/// whether it has failed, as JSON written without reflection.
/// </summary>
internal static class StatusEndpoint
{
    internal static async Task HandleAsync(HttpContext context, ProtocolOptions options)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET").ConfigureAwait(false);
            return;
        }

        if (await Exchange.BeginAsync(context, options, DatasetPermissions.Admin).ConfigureAwait(false) is not { } exchange)
        {
            return;
        }

        Dataset dataset = exchange.Dataset;
        Position head = dataset.Head;
        Preconditions.Describe(context.Response, head);
        ArrayBufferWriter<byte> body = new(512);

        using (Utf8JsonWriter json = new(body))
        {
            json.WriteStartObject();
            json.WriteString("name", exchange.Name.Value);
            json.WriteString("id", dataset.Id.Value);
            json.WriteNumber("head", head.Value);
            json.WriteString("durability", dataset.Durability.ToString());
            json.WriteBoolean("failed", dataset.IsFailed);
            json.WriteStartObject("settings");
            json.WriteString("defaultAccessScope", dataset.Settings.DefaultAccessScope.ToString());
            json.WriteEndObject();
            json.WriteStartArray("checkpoints");

            foreach (Position checkpoint in dataset.Checkpoints)
            {
                json.WriteNumberValue(checkpoint.Value);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        context.Response.ContentType = MediaTypes.Json;
        context.Response.ContentLength = body.WrittenCount;
        await context.Response.Body.WriteAsync(body.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }
}
