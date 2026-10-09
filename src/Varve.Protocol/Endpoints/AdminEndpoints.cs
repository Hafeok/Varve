// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The admin API (ADR 0106): the datasets a host knows, created, opened,
/// closed and deleted by a server admin; a dataset's settings commit and
/// checkpoint by its admin. Every answer is JSON or an RFC 9457 problem.
/// </summary>
internal static class AdminEndpoints
{
    /// <summary><c>GET /datasets</c>: the datasets the caller administers, empty rather than refused for one who administers none.</summary>
    internal static async Task ListAsync(HttpContext context, ProtocolOptions options)
    {
        if (options.Administration is not { } administration)
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true && !await Exchange.MayAsync(context, options, DatasetPermissions.ServerAdmin, null).ConfigureAwait(false))
        {
            // Anonymous mode allows everyone; any other host challenges.
            await Exchange.AuthorizeServerAsync(context, options, DatasetPermissions.ServerAdmin).ConfigureAwait(false);
            return;
        }

        bool server = await Exchange.MayAsync(context, options, DatasetPermissions.ServerAdmin, null).ConfigureAwait(false);
        List<DatasetEntry> visible = [];

        foreach (DatasetEntry entry in administration.List())
        {
            if (server || await Exchange.MayAsync(context, options, DatasetPermissions.Admin, entry.Name).ConfigureAwait(false))
            {
                visible.Add(entry);
            }
        }

        await WriteJsonAsync(context, StatusCodes.Status200OK, json =>
        {
            json.WriteStartArray("datasets");

            foreach (DatasetEntry entry in visible)
            {
                json.WriteStartObject();
                WriteEntry(json, entry);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }).ConfigureAwait(false);
    }

    /// <summary><c>PUT /datasets/{name}</c>: a new dataset, <c>201</c>; <c>409</c> when the name is in use.</summary>
    internal static async Task CreateAsync(HttpContext context, ProtocolOptions options)
    {
        if (await BeginServerAsync(context, options).ConfigureAwait(false) is not (IDatasetAdministration administration, DatasetName name))
        {
            return;
        }

        DatasetStorage storage = DatasetStorage.File;
        Body body = await RequestBodies.ReadAsync(context, options.Limits).ConfigureAwait(false);

        if (body.TooLarge)
        {
            await HttpProblems.RequestTooLarge(context).ConfigureAwait(false);
            return;
        }

        if (body.Bytes is { Length: > 0 } bytes)
        {
            if (!TryReadStorage(bytes, out storage))
            {
                await HttpProblems.BadRequest(context, "The body, when given, is {\"storage\": \"File\"} or {\"storage\": \"Memory\"}.").ConfigureAwait(false);
                return;
            }
        }

        switch (await administration.CreateAsync(name, storage, context.RequestAborted).ConfigureAwait(false))
        {
            case AdminOutcome.Done:
                context.Response.StatusCode = StatusCodes.Status201Created;
                break;
            case AdminOutcome.Exists:
                await HttpProblems.WriteAsync(context, StatusCodes.Status409Conflict, ProblemType.DatasetExists, "A dataset by that name exists.").ConfigureAwait(false);
                break;
            default:
                await Failed(context, administration, name).ConfigureAwait(false);
                break;
        }
    }

    /// <summary><c>DELETE /datasets/{name}</c>: a closed dataset's directory is removed, <c>204</c>; <c>409</c> while it is open.</summary>
    internal static async Task DeleteAsync(HttpContext context, ProtocolOptions options)
    {
        if (await BeginServerAsync(context, options).ConfigureAwait(false) is not (IDatasetAdministration administration, DatasetName name))
        {
            return;
        }

        switch (await administration.DeleteAsync(name, context.RequestAborted).ConfigureAwait(false))
        {
            case AdminOutcome.Done:
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                break;
            case AdminOutcome.Open:
                await HttpProblems.WriteAsync(context, StatusCodes.Status409Conflict, ProblemType.DatasetOpen, "The dataset is open; close it first.").ConfigureAwait(false);
                break;
            case AdminOutcome.NotFound:
                await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
                break;
            default:
                await Failed(context, administration, name).ConfigureAwait(false);
                break;
        }
    }

    /// <summary><c>POST /datasets/{name}/open</c>: a closed or failed dataset is opened, <c>204</c>.</summary>
    internal static async Task OpenAsync(HttpContext context, ProtocolOptions options)
    {
        if (await BeginServerAsync(context, options).ConfigureAwait(false) is not (IDatasetAdministration administration, DatasetName name))
        {
            return;
        }

        switch (await administration.OpenAsync(name, context.RequestAborted).ConfigureAwait(false))
        {
            case AdminOutcome.Done:
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                break;
            case AdminOutcome.NotFound:
                await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
                break;
            default:
                await Failed(context, administration, name).ConfigureAwait(false);
                break;
        }
    }

    /// <summary><c>POST /datasets/{name}/close</c>: an open dataset is drained and closed, <c>204</c>.</summary>
    internal static async Task CloseAsync(HttpContext context, ProtocolOptions options)
    {
        if (await BeginServerAsync(context, options).ConfigureAwait(false) is not (IDatasetAdministration administration, DatasetName name))
        {
            return;
        }

        switch (await administration.CloseAsync(name, context.RequestAborted).ConfigureAwait(false))
        {
            case AdminOutcome.Done:
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                break;
            case AdminOutcome.NotFound:
                await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
                break;
            default:
                await Failed(context, administration, name).ConfigureAwait(false);
                break;
        }
    }

    /// <summary><c>POST {dataset}/settings</c>: a <c>Settings</c> commit with the caller as agent, <c>If-Match</c> as the expected position.</summary>
    internal static async Task SettingsAsync(HttpContext context, ProtocolOptions options)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "POST").ConfigureAwait(false);
            return;
        }

        if (await Exchange.BeginAsync(context, options, DatasetPermissions.Admin).ConfigureAwait(false) is not { } exchange)
        {
            return;
        }

        if (await Writes.CheckAsync(exchange).ConfigureAwait(false) is not { Proceed: true } plan)
        {
            return;
        }

        Body body = await RequestBodies.ReadAsync(context, options.Limits).ConfigureAwait(false);

        if (body.TooLarge)
        {
            await HttpProblems.RequestTooLarge(context).ConfigureAwait(false);
            return;
        }

        if (body.Bytes is not { Length: > 0 } bytes || !TryReadSettings(bytes, out SettingsChange change))
        {
            await HttpProblems.BadRequest(context, "The body is {\"defaultAccessScope\": \"AllHistory\"} or {\"defaultAccessScope\": \"Current\"}.").ConfigureAwait(false);
            return;
        }

        CommitMetadata metadata = Writes.Metadata(exchange);

        if (metadata.Agent.IsNone)
        {
            await HttpProblems.WriteAsync(context, StatusCodes.Status403Forbidden, ProblemType.AgentRequired,
                "A settings commit records its agent, and the caller has none.",
                "In anonymous mode no caller is named (ADR 0094); a settings change needs an authenticated caller.").ConfigureAwait(false);
            return;
        }

        CommitResult result = await exchange.Dataset.ChangeSettingsAsync(change, metadata, plan.Expected, context.RequestAborted).ConfigureAwait(false);
        await Writes.AnswerAsync(exchange, result).ConfigureAwait(false);
    }

    /// <summary><c>POST {dataset}/checkpoints?at=</c>: a checkpoint at the head or at a position, <c>201</c> with the position.</summary>
    internal static async Task CheckpointAsync(HttpContext context, ProtocolOptions options)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "POST").ConfigureAwait(false);
            return;
        }

        if (await Exchange.BeginAsync(context, options, DatasetPermissions.Admin).ConfigureAwait(false) is not { } exchange)
        {
            return;
        }

        Dataset dataset = exchange.Dataset;
        Position at = dataset.Head;
        string? requested = context.Request.Query["at"];

        if (requested is not null)
        {
            if (!long.TryParse(requested, NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value < 0)
            {
                await HttpProblems.BadRequest(context, "at is a position.").ConfigureAwait(false);
                return;
            }

            at = new Position(value);

            if (at > dataset.Head)
            {
                Preconditions.Describe(context.Response, dataset.Head);
                await HttpProblems.WriteAsync(context, StatusCodes.Status404NotFound, ProblemType.PositionNotReached, "The position is after the head.").ConfigureAwait(false);
                return;
            }
        }

        if (at.Value == 0)
        {
            await HttpProblems.BadRequest(context, "Nothing to checkpoint: the dataset has no commit.").ConfigureAwait(false);
            return;
        }

        try
        {
            await dataset.CheckpointAsync(at, context.RequestAborted).ConfigureAwait(false);
        }
        catch (DatasetUnavailableException failed)
        {
            await QueryRun.WriteUnavailableAsync(exchange, failed.Message).ConfigureAwait(false);
            return;
        }

        Preconditions.Describe(context.Response, at);
        context.Response.StatusCode = StatusCodes.Status201Created;
    }

    /// <summary>The entry's fields, for the list and the status.</summary>
    internal static void WriteEntry(Utf8JsonWriter json, DatasetEntry entry)
    {
        json.WriteString("name", entry.Name.Value);
        json.WriteString("state", entry.State switch { DatasetState.Open => "open", DatasetState.Closed => "closed", _ => "failed" });
        json.WriteString("storage", entry.Storage == DatasetStorage.Memory ? "Memory" : "File");
        json.WriteString("origin", entry.Origin switch { DatasetOrigin.Configured => "configured", DatasetOrigin.Discovered => "discovered", _ => "created" });

        if (entry.Reason is not null)
        {
            json.WriteString("reason", entry.Reason);
        }

        if (entry.Id is DatasetId id)
        {
            json.WriteString("id", id.Value);
        }

        if (entry.Head is Position head)
        {
            json.WriteNumber("head", head.Value);
        }
    }

    // Authorises the server-wide permission, then takes the name: an invalid
    // name is 400 here, since a server admin may know every name.
    private static async Task<(IDatasetAdministration, DatasetName)?> BeginServerAsync(HttpContext context, ProtocolOptions options)
    {
        if (!await Exchange.AuthorizeServerAsync(context, options, DatasetPermissions.ServerAdmin).ConfigureAwait(false))
        {
            return null;
        }

        if (options.Administration is not { } administration)
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return null;
        }

        if (!context.Request.RouteValues.TryGetValue(Exchange.DatasetRouteValue, out object? value) || !DatasetName.TryParse(value as string, out DatasetName name))
        {
            await HttpProblems.BadRequest(context, "A dataset name is 1 to 63 characters of [A-Za-z0-9._-], starting with a letter or a digit.").ConfigureAwait(false);
            return null;
        }

        return (administration, name);
    }

    private static Task Failed(HttpContext context, IDatasetAdministration administration, DatasetName name)
    {
        string? reason = null;

        foreach (DatasetEntry entry in administration.List())
        {
            if (entry.Name == name)
            {
                reason = entry.Reason;
            }
        }

        return HttpProblems.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, ProblemType.Unavailable, "The dataset could not be opened.", reason);
    }

    private static bool TryReadStorage(byte[] body, out DatasetStorage storage)
    {
        storage = DatasetStorage.File;

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("storage", out JsonElement value))
            {
                return true;
            }

            switch (value.GetString())
            {
                case "File":
                    storage = DatasetStorage.File;
                    return true;
                case "Memory":
                    storage = DatasetStorage.Memory;
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadSettings(byte[] body, out SettingsChange change)
    {
        change = new SettingsChange();

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("defaultAccessScope", out JsonElement value))
            {
                return false;
            }

            switch (value.GetString())
            {
                case "AllHistory":
                    change = new SettingsChange { DefaultAccessScope = AccessScope.AllHistory };
                    return true;
                case "Current":
                    change = new SettingsChange { DefaultAccessScope = AccessScope.Current };
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task WriteJsonAsync(HttpContext context, int status, Action<Utf8JsonWriter> write)
    {
        ArrayBufferWriter<byte> body = new(512);

        using (Utf8JsonWriter json = new(body))
        {
            json.WriteStartObject();
            write(json);
            json.WriteEndObject();
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = MediaTypes.Json;
        context.Response.ContentLength = body.WrittenCount;
        await context.Response.Body.WriteAsync(body.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }
}
