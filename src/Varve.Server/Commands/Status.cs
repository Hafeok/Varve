// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve status</c>: what one instance says of itself on
/// <c>/health/ready</c> (ADR 0113), as lines an operator reads. Each dataset
/// with its state, its role and its lag, and each shipping target with its
/// state, how far it got, and what its last failure said (ADR 0184 decision
/// 11). It reads and changes nothing, and takes no token: readiness takes
/// none.
/// </summary>
internal static class Status
{
    internal static Command Command(Io io)
    {
        Option<string> server = new("--server") { Description = "The instance to ask, by its address. Readiness is each instance's own.", Required = true };
        Command status = new("status", "What an instance reports of itself: each dataset's state, role and lag, and each shipping target's state with its detail (ADRs 0113, 0184).");
        status.Options.Add(server);

        status.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            using HttpClient http = OutboundHttp.CreateClient();
            return await ShowAsync(http, parsed.GetValue(server)!.TrimEnd('/'), io, cancellationToken).ConfigureAwait(false) ? 0 : 1;
        }, parsed.InvocationConfiguration.Error));

        return status;
    }

    /// <summary>Reads the instance's readiness and writes it; whether the instance is ready.</summary>
    internal static async Task<bool> ShowAsync(HttpClient http, string server, Io io, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(new Uri(server + "/health/ready", UriKind.Absolute), cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new Cli.CommandException(server + " answered " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " to /health/ready, and not with readiness.");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            bool ready = response.IsSuccessStatusCode;
            StringBuilder text = new();
            text.Append(server).Append(": ").AppendLine(ready ? "ready" : "not ready" + (Text(root, "detail") is { } detail ? " (" + detail + ")" : string.Empty));

            if (root.TryGetProperty("datasets", out JsonElement datasets))
            {
                // Ready answers an object by name; not ready answers a list of the datasets that are not.
                if (datasets.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty dataset in datasets.EnumerateObject())
                    {
                        Dataset(text, dataset.Name, dataset.Value);
                    }
                }
                else if (datasets.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement dataset in datasets.EnumerateArray())
                    {
                        Dataset(text, Text(dataset, "name") ?? "?", dataset);
                    }
                }
            }

            await io.Out.WriteAsync(text.ToString()).ConfigureAwait(false);
            return ready;
        }
    }

    private static void Dataset(StringBuilder text, string name, JsonElement dataset)
    {
        text.Append("  ").Append(name).Append(": ").Append(Text(dataset, "state") ?? "?");

        if (Text(dataset, "role") is { } role)
        {
            text.Append(", ").Append(role);
        }

        if (Number(dataset, "position") is { } position)
        {
            text.Append(", at position ").Append(position);
        }

        if (Number(dataset, "lag") is { } lag and not "0")
        {
            text.Append(", ").Append(lag).Append(" behind");
        }

        text.AppendLine();

        if (Text(dataset, "reason") is { } reason)
        {
            text.Append("    ").AppendLine(reason);
        }

        if (!dataset.TryGetProperty("shipping", out JsonElement shipping) || shipping.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty target in shipping.EnumerateObject())
        {
            text.Append("    ships to ").Append(target.Name).Append(": ").Append(Text(target.Value, "state") ?? "?");

            if (Number(target.Value, "shippedThrough") is { } through)
            {
                text.Append(", through position ").Append(through);
            }

            if (Number(target.Value, "lag") is { } behind)
            {
                text.Append(" (").Append(behind).Append(" behind)");
            }

            if (Number(target.Value, "catalogPosition") is { } catalog)
            {
                text.Append(", checkpoint at ").Append(catalog);
            }

            if (Number(target.Value, "failures") is { } failures)
            {
                text.Append(", ").Append(failures).Append(failures == "1" ? " failed round" : " failed rounds");
            }

            text.AppendLine();

            if (Text(target.Value, "detail") is { } said)
            {
                text.Append("      ").AppendLine(said);
            }
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
}
