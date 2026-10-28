// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Server.Commands;

/// <summary>
/// The takeover from a failed instance (ADR 0181 point 2), as <c>varve
/// manage takeover</c> and <c>varve manage watch-kubernetes</c> both make it:
/// for each dataset the failed instance still holds, a takeover request at
/// that epoch to the first server named that is a warm standby of it, after
/// asking the failed instance itself whether the trigger is stale.
/// </summary>
[Contract(typeof(HandoverAutomation.TakeoverCommandIsIdempotent), Role = "the takeover from a failed instance, a no-op for what is already handled")]
internal static class Takeovers
{
    /// <summary>
    /// The takeover from a failed instance, as <c>varve manage takeover</c>
    /// and the Kubernetes controller both make it (ADR 0181 point 2): true
    /// when every dataset named, or every one listed and <c>_fleet</c>, is
    /// taken over or needs nothing.
    /// </summary>
    internal static async Task<bool> FromAsync(HttpClient http, string? bearer, string failed, string[] addresses, string[] named, Io io, CancellationToken cancellationToken)
    {
        int refused = 0;

        foreach (string name in named.Length > 0 ? named : await ListAsync(http, bearer, addresses, cancellationToken).ConfigureAwait(false))
        {
            refused += await TakeOverAsync(http, bearer, addresses, name, failed, io, cancellationToken).ConfigureAwait(false) ? 0 : 1;
        }

        return refused == 0;
    }

    // Every dataset the first answering server lists, and the system dataset.
    private static async Task<string[]> ListAsync(HttpClient http, string? bearer, string[] addresses, CancellationToken cancellationToken)
    {
        foreach (string address in addresses)
        {
            using JsonDocument? list = await GetAsync(http, bearer, address + "/manage/datasets", cancellationToken).ConfigureAwait(false);

            if (list is not null)
            {
                return [.. list.RootElement.GetProperty("datasets").EnumerateArray().Select(entry => entry.GetProperty("name").GetString()!), SystemDataset];
            }
        }

        throw new Cli.CommandException("no server named answered GET /manage/datasets.");
    }

    // One dataset: true once it is taken over or needs no takeover, false
    // when every server refused it.
    private static async Task<bool> TakeOverAsync(HttpClient http, string? bearer, string[] addresses, string name, string failed, Io io, CancellationToken cancellationToken)
    {
        bool system = name == SystemDataset;
        string path = "/manage/datasets/" + Uri.EscapeDataString(name) + "/state";
        List<string> refusals = [];
        bool askedHolder = false;

        foreach (string address in addresses)
        {
            // The epoch and its holder as this server reads them: the record's
            // holder for a dataset, the system writer's epoch for _fleet.
            using JsonDocument? state = await GetAsync(http, bearer, address + (system ? "/manage/system" : path), cancellationToken).ConfigureAwait(false);

            if (state is null)
            {
                refusals.Add(address + ": no answer to its state");
                continue;
            }

            JsonElement root = state.RootElement;

            if (!root.TryGetProperty("epoch", out JsonElement epochOf))
            {
                await io.Out.WriteLineAsync(name + ": not in an object store, so it has no writer epoch; nothing to take over.").ConfigureAwait(false);
                return true;
            }

            long epoch = epochOf.GetInt64();

            if (!system && Holder(root) is { } holder && holder != failed)
            {
                await io.Out.WriteLineAsync(name + ": epoch " + epoch + " is held by " + holder + ", not " + failed + "; nothing to take over.").ConfigureAwait(false);
                return true;
            }

            // A stale trigger: the failed instance recovered by restart and
            // reclaimed before the trigger came. It answers ready and holds
            // the dataset, so nothing is taken over (ADR 0181 point 2).
            if (!askedHolder && (system ? String(root, "writer") : HolderAddress(root)) is { } holding)
            {
                askedHolder = true;

                if (await HolderAnswersAsync(http, bearer, holding.TrimEnd('/'), system ? "/manage/system" : path, cancellationToken).ConfigureAwait(false))
                {
                    await io.Out.WriteLineAsync(name + ": " + (system ? "its writer" : failed) + " at " + holding + " answers ready and holds epoch " + epoch + ": the trigger is stale; nothing to take over.").ConfigureAwait(false);
                    return true;
                }
            }

            if (!system && root.TryGetProperty("writerSet", out JsonElement set) && !set.GetProperty("automaticHandover").GetBoolean())
            {
                await io.Out.WriteLineAsync(name + ": does not declare automatic handover; nothing to take over.").ConfigureAwait(false);
                return true;
            }

            if (!system && !(root.TryGetProperty("role", out JsonElement role) && role.GetString() == "standby" && root.TryGetProperty("warm", out JsonElement warm) && warm.GetBoolean()))
            {
                refusals.Add(address + ": not a warm standby (" + (root.TryGetProperty("role", out JsonElement shown) ? shown.GetString() : "no role") + ")");
                continue;
            }

            using HttpRequestMessage request = new(HttpMethod.Put, new Uri(address + path, UriKind.Absolute))
            {
                Content = new StringContent("{\"takeover\":{\"epoch\":" + epoch.ToString(CultureInfo.InvariantCulture) + "}}", Encoding.UTF8, "application/json"),
            };

            if (bearer is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }

            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                using JsonDocument answer = JsonDocument.Parse(body);
                await io.Out.WriteLineAsync("Taken over: " + name + " from epoch " + epoch + " by " + address + ", at epoch " + answer.RootElement.GetProperty("epoch").GetInt64() + ".").ConfigureAwait(false);
                return true;
            }

            string? reason = Reason(body);

            // The epoch moved: another standby, or the writer itself, holds a later one.
            if (reason == "epoch-moved")
            {
                await io.Out.WriteLineAsync(name + ": the epoch moved past " + epoch + " meanwhile; nothing to take over.").ConfigureAwait(false);
                return true;
            }

            // The system dataset without Varve:Fleet:AutomaticHandover keeps restart as its recovery.
            if (reason == "handover-off")
            {
                await io.Out.WriteLineAsync(name + ": does not declare automatic handover; nothing to take over.").ConfigureAwait(false);
                return true;
            }

            refusals.Add(address + ": " + (int)response.StatusCode + (reason is null ? string.Empty : " " + reason));
        }

        await io.Error.WriteLineAsync("varve: " + name + " was not taken over: " + string.Join("; ", refusals)).ConfigureAwait(false);
        return false;
    }

    private const string SystemDataset = "_fleet";

    // How long the failed instance is given to answer before it counts as
    // not answering, and the takeover goes on.
    private static readonly TimeSpan HolderProbe = TimeSpan.FromSeconds(5);

    // Whether the holder at its recorded address answers /health/ready and
    // holds the dataset as its writer; a refusal, no answer or a timeout is no.
    private static async Task<bool> HolderAnswersAsync(HttpClient http, string? bearer, string address, string path, CancellationToken cancellationToken)
    {
        using CancellationTokenSource probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probe.CancelAfter(HolderProbe);

        try
        {
            using HttpRequestMessage ready = new(HttpMethod.Get, new Uri(address + "/health/ready", UriKind.Absolute));
            using HttpResponseMessage answer = await http.SendAsync(ready, probe.Token).ConfigureAwait(false);

            if (!answer.IsSuccessStatusCode)
            {
                return false;
            }

            using JsonDocument? state = await GetAsync(http, bearer, address + path, probe.Token).ConfigureAwait(false);
            return state is not null && String(state.RootElement, "role") == "writer";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    // The address the epoch history names for its last holder, if any.
    private static string? HolderAddress(JsonElement state) =>
        state.TryGetProperty("history", out JsonElement history) && history.GetArrayLength() > 0 ? String(history[history.GetArrayLength() - 1], "address") : null;

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<JsonDocument?> GetAsync(HttpClient http, string? bearer, string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(url, UriKind.Absolute));

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) : null;
        }
        catch (HttpRequestException)
        {
            // The failed instance itself, or one unreachable: the next is asked.
            return null;
        }
    }

    // The holder the epoch history names last, if any.
    private static string? Holder(JsonElement state) =>
        state.TryGetProperty("history", out JsonElement history) && history.GetArrayLength() > 0 ? history[history.GetArrayLength() - 1].GetProperty("holder").GetString() : null;

    private static string? Reason(string problem)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(problem);
            return document.RootElement.TryGetProperty("reason", out JsonElement reason) ? reason.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
