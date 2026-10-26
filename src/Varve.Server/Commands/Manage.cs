// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Varve.Protocol.Client;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve manage move-events &lt;dataset&gt; --server &lt;url&gt;</c> (ADR
/// 0167): moves a dataset's event declarations to the management plane, by
/// <c>POST /manage/datasets/{name}/events/move</c>. The move is idempotent,
/// so a move that stopped (a writer that changed, a lost answer) is sent
/// again, and finishes.
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
        return manage;
    }
}
