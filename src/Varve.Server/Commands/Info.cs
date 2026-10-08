// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.CommandLine;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server.Commands;

/// <summary><c>varve info &lt;dataset&gt;</c>: the status, as JSON, in the shape <c>GET /status</c> answers.</summary>
internal static class Info
{
    internal static Command Command(Target target, Io io)
    {
        Command command = new("info", "The dataset's id, head, durability, settings, checkpoints and projection, as JSON.");
        target.AddTo(command);
        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            await using Opened opened = await target.OpenAsync(parsed, io, cancellationToken).ConfigureAwait(false);

            if (opened is Remote remote)
            {
                using HttpResponseMessage response = await remote.Client.StatusAsync(cancellationToken).ConfigureAwait(false);
                return await Commands.CopyAsync(response, io, cancellationToken).ConfigureAwait(false);
            }

            Dataset dataset = ((Local)opened).Dataset;
            ArrayBufferWriter<byte> body = new(512);

            using (Utf8JsonWriter json = new(body, new JsonWriterOptions { Indented = true }))
            {
                Position head = dataset.Head;
                json.WriteStartObject();
                json.WriteString("id", dataset.Id.Value);
                json.WriteNumber("head", head.Value);
                json.WriteString("durability", dataset.Durability.ToString());
                json.WriteBoolean("failed", dataset.IsFailed);
                json.WriteString("state", "open");
                json.WriteStartObject("settings");
                json.WriteString("defaultAccessScope", dataset.Settings.DefaultAccessScope.ToString());
                json.WriteEndObject();
                json.WriteStartArray("checkpoints");

                foreach (Position checkpoint in dataset.Checkpoints)
                {
                    json.WriteNumberValue(checkpoint.Value);
                }

                json.WriteEndArray();
                json.WriteStartObject("projection");
                json.WriteNumber("position", dataset.ProjectionPosition.Value);
                json.WriteNumber("lag", head.Value - dataset.ProjectionPosition.Value);
                json.WriteBoolean("failed", dataset.IsFailed);
                json.WriteEndObject();
                json.WriteEndObject();
            }

            await io.Data.WriteAsync(body.WrittenMemory, cancellationToken).ConfigureAwait(false);
            await io.Data.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await io.Data.FlushAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }, parsed.InvocationConfiguration.Error));
        return command;
    }
}

/// <summary>What the commands share: the IO behind a parse result, and the copy of a response to it.</summary>
internal static class Commands
{
    /// <summary>A response's body to the data stream when it succeeded; its problem to the error stream and exit 1 otherwise.</summary>
    internal static async Task<int> CopyAsync(HttpResponseMessage response, Io io, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            string problem = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            await io.Error.WriteLineAsync("varve: the server answered " + (int)response.StatusCode + (problem.Length > 0 ? ": " + problem : ".")).ConfigureAwait(false);
            return 1;
        }

        using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await body.CopyToAsync(io.Data, cancellationToken).ConfigureAwait(false);
        await io.Data.FlushAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    /// <summary>A write's answer: the position it left the dataset at, or the problem; exit 1 for a refused write.</summary>
    internal static async Task<int> AnswerWriteAsync(HttpResponseMessage response, Io io, CancellationToken cancellationToken)
    {
        long? position = Varve.Protocol.Client.SparqlHttpClient.PositionOf(response);

        if (response.IsSuccessStatusCode)
        {
            await io.Out.WriteLineAsync((int)response.StatusCode == 201 ? "created" : "committed, position " + (position?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")).ConfigureAwait(false);
            return 0;
        }

        string problem = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        await io.Error.WriteLineAsync("varve: the server answered " + (int)response.StatusCode + (problem.Length > 0 ? ": " + problem : ".")).ConfigureAwait(false);
        return 1;
    }

    /// <summary>The text of <c>--query</c>/<c>--update</c> or <c>--file</c>, one of which is required.</summary>
    internal static async Task<byte[]> TextAsync(string? inline, string? file, string what, CancellationToken cancellationToken)
    {
        if (inline is not null && file is not null)
        {
            throw new Cli.CommandException("Give the " + what + " inline or as a file, not both.");
        }

        if (inline is not null)
        {
            return System.Text.Encoding.UTF8.GetBytes(inline);
        }

        if (file is not null)
        {
            return await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
        }

        throw new Cli.CommandException("Give the " + what + " inline (--" + what + ") or as a file (--file).");
    }
}
