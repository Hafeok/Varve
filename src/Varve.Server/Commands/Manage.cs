// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve manage move-events &lt;dataset&gt; --server &lt;url&gt;</c> (ADR
/// 0167): moves a dataset's event declarations to the management plane, by
/// <c>POST /manage/datasets/{name}/events/move</c>. The move is idempotent,
/// so a move that stopped (a writer that changed, a lost answer) is sent
/// again, and finishes.
/// <c>varve manage takeover</c> (ADR 0181) is the automation's act on a
/// probe failure: a takeover request (ADR 0179 point 6) to a warm standby.
/// </summary>
internal static class Manage
{
    // Sends the move makes when the answer says to send it again.
    private const int Attempts = 4;

    internal static Command Command(Io io)
    {
        Argument<string> dataset = new("dataset") { Description = "The dataset whose event declarations move." };
        Option<string?> server = new("--server") { Description = "The server's address; any instance of the fleet." };
        Option<string?> token = new("--token") { Description = "A bearer token for the management audience; VARVE_TOKEN when absent." };
        Option<string?> authority = new("--authority") { Description = "The OIDC issuer to obtain a token from." };
        Option<string?> clientId = new("--client-id") { Description = "The OIDC client." };
        Option<string?> clientSecret = new("--client-secret") { Description = "The client's secret; VARVE_CLIENT_SECRET when absent. Prefer --client-assertion." };
        Option<string?> clientAssertion = new("--client-assertion") { Description = "Workload identity federation: a file holding the platform's token, or 'github' for GitHub Actions' OIDC token." };
        Option<string?> assertionAudience = new("--assertion-audience") { Description = "The audience GitHub Actions' token is asked for; api://AzureADTokenExchange by default." };
        Option<string?> scope = new("--scope") { Description = "The scope to ask the issuer for: the management audience's." };
        Command move = new("move-events", "Move a dataset's event declarations from its catalogue graphs to the management plane (ADR 0167).");
        move.Arguments.Add(dataset);

        foreach (Option option in (Option[])[server, token, authority, clientId, clientSecret, clientAssertion, assertionAudience, scope])
        {
            move.Options.Add(option);
        }

        move.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            string address = parsed.GetValue(server) ?? throw new Cli.CommandException("--server names any instance of the fleet.");
            string name = parsed.GetValue(dataset)!;
            using HttpClient http = OutboundHttp.CreateClient();
            string? bearer = await Apply.TokenAsync(parsed, http, token, authority, clientId, clientSecret, clientAssertion, assertionAudience, scope, cancellationToken).ConfigureAwait(false);
            Uri url = new(address.TrimEnd('/') + "/manage/datasets/" + Uri.EscapeDataString(name) + "/events/move", UriKind.Absolute);

            for (int attempt = 1; ; attempt++)
            {
                using HttpRequestMessage request = new(HttpMethod.Post, url);

                if (bearer is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                }

                using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using JsonDocument answer = JsonDocument.Parse(body);
                    JsonElement root = answer.RootElement;
                    await io.Out.WriteLineAsync("Moved: the event declarations of " + name + " (" + root.GetProperty("types").GetInt32()
                        + " types) are on the management plane at system position " + root.GetProperty("position").GetInt64() + ".").ConfigureAwait(false);
                    return 0;
                }

                // The writer changed, or the declarations moved on meanwhile: the move resumes where it stopped.
                if (attempt < Attempts && response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict && !body.Contains("event-declaration-invalid", StringComparison.Ordinal))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 << attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new Cli.CommandException("the move was refused (" + (int)response.StatusCode + "): " + body);
            }
        }, parsed.InvocationConfiguration.Error));

        Command manage = new("manage", "Management plane operations that are acts, not declarations.");
        manage.Subcommands.Add(move);
        manage.Subcommands.Add(Takeover(io));
        return manage;
    }

    // varve manage takeover <dataset>... --from <instance> --server <url>...:
    // for each dataset whose epoch the failed instance holds, a takeover
    // request at that epoch to the first server named that is a warm standby
    // of it (ADR 0179 point 6). Each server answers for itself, so the
    // automation names the instances it may move the writer to. A dataset
    // the failed instance no longer holds, or whose epoch moved meanwhile, is
    // already handled: a late or duplicated trigger is a no-op. The system
    // dataset is _fleet.
    private static Command Takeover(Io io)
    {
        Argument<string[]> datasets = new("dataset") { Description = "The datasets to take over, _fleet for the system dataset; every dataset the failed instance holds when absent.", Arity = ArgumentArity.ZeroOrMore };
        Option<string> from = new("--from") { Description = "The instance the probe found failed: only an epoch it holds is taken over.", Required = true };
        Option<string[]> servers = new("--server") { Description = "An instance that may take the writer over, by its address; repeat it, in order of preference.", Required = true, AllowMultipleArgumentsPerToken = true };
        Option<string?> token = new("--token") { Description = "A bearer token for the management audience, of the operator role; VARVE_TOKEN when absent." };
        Option<string?> authority = new("--authority") { Description = "The OIDC issuer to obtain a token from." };
        Option<string?> clientId = new("--client-id") { Description = "The OIDC client." };
        Option<string?> clientSecret = new("--client-secret") { Description = "The client's secret; VARVE_CLIENT_SECRET when absent. Prefer --client-assertion." };
        Option<string?> clientAssertion = new("--client-assertion") { Description = "Workload identity federation: a file holding the platform's token, or 'github' for GitHub Actions' OIDC token." };
        Option<string?> assertionAudience = new("--assertion-audience") { Description = "The audience GitHub Actions' token is asked for; api://AzureADTokenExchange by default." };
        Option<string?> scope = new("--scope") { Description = "The scope to ask the issuer for: the management audience's." };
        Command takeover = new("takeover", "Move the writer of the datasets a failed instance holds to a warm standby (ADRs 0179, 0181).");
        takeover.Arguments.Add(datasets);

        foreach (Option option in (Option[])[from, servers, token, authority, clientId, clientSecret, clientAssertion, assertionAudience, scope])
        {
            takeover.Options.Add(option);
        }

        takeover.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            string failed = parsed.GetValue(from)!;
            string[] addresses = [.. parsed.GetValue(servers)!.Select(address => address.TrimEnd('/'))];
            using HttpClient http = OutboundHttp.CreateClient();
            string? bearer = await Apply.TokenAsync(parsed, http, token, authority, clientId, clientSecret, clientAssertion, assertionAudience, scope, cancellationToken).ConfigureAwait(false);
            string[] named = parsed.GetValue(datasets) ?? [];
            int refused = 0;

            foreach (string name in named.Length > 0 ? named : await ListAsync(http, bearer, addresses, cancellationToken).ConfigureAwait(false))
            {
                refused += await TakeOverAsync(http, bearer, addresses, name, failed, io, cancellationToken).ConfigureAwait(false) ? 0 : 1;
            }

            return refused == 0 ? 0 : 1;
        }, parsed.InvocationConfiguration.Error));

        return takeover;
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
