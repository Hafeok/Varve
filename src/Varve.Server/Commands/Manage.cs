// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System;
using DecisionDriven.Ledger.Varve;
using DecisionDriven;
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
        manage.Subcommands.Add(KubernetesController.Command(io));
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
        Command takeover = new("takeover", "Move the writer of the datasets a failed instance holds to a warm standby (ADRs 0179, 0181).");
        takeover.Arguments.Add(datasets);
        takeover.Options.Add(from);
        takeover.Options.Add(servers);
        OperatorToken credentials = new(takeover);

        takeover.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            using HttpClient http = OutboundHttp.CreateClient();
            string? bearer = await credentials.TokenAsync(parsed, http, cancellationToken).ConfigureAwait(false);
            string[] addresses = [.. parsed.GetValue(servers)!.Select(address => address.TrimEnd('/'))];
            return await Takeovers.FromAsync(http, bearer, parsed.GetValue(from)!, addresses, parsed.GetValue(datasets) ?? [], io, cancellationToken).ConfigureAwait(false) ? 0 : 1;
        }, parsed.InvocationConfiguration.Error));

        return takeover;
    }

    /// <summary>
    /// The operator token's options, on a command that sends takeovers: any
    /// of the CLI's ways (ADR 0163), or the platform's managed identity (ADR
    /// 0181 point 3). Asked for at each use, so a long-running command never
    /// holds one past its lifetime.
    /// </summary>
    [Contract(typeof(HandoverAutomation.AutomationIdentityHoldsOperatorWithoutSecret), Role = "the operator token of a command that sends takeovers")]
    internal sealed class OperatorToken
    {
        private readonly Option<string?> _token = new("--token") { Description = "A bearer token for the management audience, of the operator role; VARVE_TOKEN when absent." };
        private readonly Option<string?> _authority = new("--authority") { Description = "The OIDC issuer to obtain a token from." };
        private readonly Option<string?> _clientId = new("--client-id") { Description = "The OIDC client." };
        private readonly Option<string?> _clientSecret = new("--client-secret") { Description = "The client's secret; VARVE_CLIENT_SECRET when absent. Prefer --client-assertion." };
        private readonly Option<string?> _clientAssertion = new("--client-assertion") { Description = "Workload identity federation: a file holding the platform's token, or 'github' for GitHub Actions' OIDC token." };
        private readonly Option<string?> _assertionAudience = new("--assertion-audience") { Description = "The audience GitHub Actions' token is asked for; api://AzureADTokenExchange by default." };
        private readonly Option<string?> _scope = new("--scope") { Description = "The scope to ask the issuer for: the management audience's." };
        private readonly Option<bool> _managedIdentity = new("--managed-identity") { Description = "Take the token from the platform's managed identity (Container Apps' IDENTITY_ENDPOINT), for the audience --scope names; AZURE_CLIENT_ID picks a user-assigned identity." };

        internal OperatorToken(Command command)
        {
            foreach (Option option in (Option[])[_token, _authority, _clientId, _clientSecret, _clientAssertion, _assertionAudience, _scope, _managedIdentity])
            {
                command.Options.Add(option);
            }
        }

        internal async Task<string?> TokenAsync(ParseResult parsed, HttpClient http, CancellationToken cancellationToken) =>
            parsed.GetValue(_managedIdentity)
                ? await ManagedIdentityTokenAsync(http, parsed.GetValue(_scope) ?? throw new Cli.CommandException("--managed-identity asks for the audience --scope names."), Environment.GetEnvironmentVariable, cancellationToken).ConfigureAwait(false)
                : await Apply.TokenAsync(parsed, http, _token, _authority, _clientId, _clientSecret, _clientAssertion, _assertionAudience, _scope, cancellationToken).ConfigureAwait(false);
    }

    // A token for the management audience from the managed-identity endpoint
    // Container Apps gives a job or an app (ADR 0181 point 3): no secret. The
    // scope's /.default suffix, if any, is the v2 form of the same resource.
    internal static async Task<string> ManagedIdentityTokenAsync(HttpClient http, string scope, Func<string, string?> environment, CancellationToken cancellationToken)
    {
        string endpoint = environment("IDENTITY_ENDPOINT") is { Length: > 0 } given ? given : throw new Cli.CommandException("--managed-identity needs the platform's IDENTITY_ENDPOINT and IDENTITY_HEADER: give the job or app an identity.");
        string header = environment("IDENTITY_HEADER") ?? throw new Cli.CommandException("--managed-identity needs IDENTITY_HEADER beside IDENTITY_ENDPOINT.");
        string resource = scope.EndsWith("/.default", StringComparison.Ordinal) ? scope[..^"/.default".Length] : scope;
        string query = "resource=" + Uri.EscapeDataString(resource) + "&api-version=2019-08-01"
            + (environment("AZURE_CLIENT_ID") is { Length: > 0 } client ? "&client_id=" + Uri.EscapeDataString(client) : string.Empty);
        using HttpRequestMessage request = new(HttpMethod.Get, new UriBuilder(endpoint) { Query = query }.Uri);
        request.Headers.Add("X-IDENTITY-HEADER", header);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Its answer names the cause, never a token.
            throw new Cli.CommandException("the managed-identity endpoint refused a token for " + resource + " with " + (int)response.StatusCode + ": " + body);
        }

        using JsonDocument json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("access_token").GetString() ?? throw new Cli.CommandException("the managed-identity endpoint answered without a token.");
    }

}
