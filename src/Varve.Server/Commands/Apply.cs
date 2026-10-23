// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Varve.Management;
using Varve.Management.Model;
using Varve.Protocol.Client;
using Varve.Rdf;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve apply &lt;files…&gt; --server &lt;url&gt;</c> (ADR 0163 point 5):
/// the management declarations the files hold, planned against the graphs
/// they own on the server and committed as one replacement of each, under
/// the versions the plan read. <c>--plan</c> prints the plan and stops;
/// <c>--exit-code</c> makes a plan that would change something exit 2, the
/// drift check CI runs; <c>--out</c> saves it and <c>--from-plan</c> applies a
/// saved one, refused when a graph moved since. <c>--from-config</c> prints
/// the TriG that replaces a configuration's <c>Varve:Datasets</c> and
/// <c>Varve:Auth:Datasets</c>. It never shares a name with
/// <c>varve setup apply</c>, which applies a dataset's data (ADR 0130).
/// </summary>
internal static class Apply
{
    /// <summary>The exit code of a plan that would change something, under <c>--exit-code</c>.</summary>
    internal const int Drift = 2;

    internal static Command Command(Io io)
    {
        Argument<string[]> files = new("files") { Description = "TriG files of management declarations; each owns, whole, every graph it names.", Arity = ArgumentArity.ZeroOrMore };
        Option<string?> server = new("--server") { Description = "The server's address; its /manage/ resources are used." };
        Option<bool> plan = new("--plan") { Description = "Print the plan and apply nothing." };
        Option<string?> output = new("--out") { Description = "Save the plan to this file, to apply later with --from-plan." };
        Option<bool> exitCode = new("--exit-code") { Description = "With --plan, exit 2 when the plan would change something: the drift check." };
        Option<string?> fromPlan = new("--from-plan") { Description = "Apply a saved plan; refused when an owned graph moved since it was made." };
        Option<string?> fromConfig = new("--from-config") { Description = "Print the TriG that replaces a configuration file's Varve:Datasets and Varve:Auth:Datasets, and its bootstrap operators." };
        Option<string?> token = new("--token") { Description = "A bearer token for the management audience; VARVE_TOKEN when absent." };
        Option<string?> authority = new("--authority") { Description = "The OIDC issuer to obtain a token from." };
        Option<string?> clientId = new("--client-id") { Description = "The OIDC client." };
        Option<string?> clientSecret = new("--client-secret") { Description = "The client's secret; VARVE_CLIENT_SECRET when absent. Prefer --client-assertion." };
        Option<string?> clientAssertion = new("--client-assertion") { Description = "Workload identity federation: a file holding the platform's token, or 'github' for GitHub Actions' OIDC token." };
        Option<string?> assertionAudience = new("--assertion-audience") { Description = "The audience GitHub Actions' token is asked for; api://AzureADTokenExchange by default." };
        Option<string?> scope = new("--scope") { Description = "The scope to ask the issuer for: the management audience's." };
        Command command = new("apply", "Plan and apply management declarations as code (ADR 0163).");
        command.Arguments.Add(files);

        foreach (Option option in (Option[])[server, plan, output, exitCode, fromPlan, fromConfig, token, authority, clientId, clientSecret, clientAssertion, assertionAudience, scope])
        {
            command.Options.Add(option);
        }

        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            if (parsed.GetValue(fromConfig) is { } configuration)
            {
                return await FromConfigAsync(configuration, io).ConfigureAwait(false);
            }

            string address = parsed.GetValue(server) ?? throw new Cli.CommandException("--server names the server to apply to.");
            Uri manage = new(address.TrimEnd('/') + "/manage/", UriKind.Absolute);
            using HttpClient http = OutboundHttp.CreateClient();
            string? bearer = await TokenAsync(parsed, http, token, authority, clientId, clientSecret, clientAssertion, assertionAudience, scope, cancellationToken).ConfigureAwait(false);
            ManagementPlan computed;

            if (parsed.GetValue(fromPlan) is { } saved)
            {
                if ((parsed.GetValue(files) ?? []).Length > 0)
                {
                    throw new Cli.CommandException("--from-plan applies the plan it names; give no files with it.");
                }

                try
                {
                    computed = ManagementPlan.FromJson(await File.ReadAllBytesAsync(saved, cancellationToken).ConfigureAwait(false));
                }
                catch (InvalidDataException bad)
                {
                    throw new Cli.CommandException(saved + " is not a saved plan: " + bad.Message);
                }
            }
            else
            {
                computed = await PlanAsync(http, manage, bearer, parsed.GetValue(files) ?? [], cancellationToken).ConfigureAwait(false);
            }

            await io.Out.WriteAsync(computed.Render()).ConfigureAwait(false);

            if (parsed.GetValue(output) is { } saveTo)
            {
                await File.WriteAllBytesAsync(saveTo, computed.ToJson(), cancellationToken).ConfigureAwait(false);
            }

            if (parsed.GetValue(plan))
            {
                return parsed.GetValue(exitCode) && !computed.IsEmpty ? Drift : 0;
            }

            if (computed.IsEmpty)
            {
                await io.Out.WriteLineAsync("Nothing to apply: the server holds what the declarations say.").ConfigureAwait(false);
                return 0;
            }

            return await CommitAsync(http, manage, bearer, computed, io, cancellationToken).ConfigureAwait(false);
        }, parsed.InvocationConfiguration.Error));
        return command;
    }

    // The files' declarations against the server's graphs, at the system
    // position the read answered at.
    private static async Task<ManagementPlan> PlanAsync(HttpClient http, Uri manage, string? bearer, string[] files, CancellationToken cancellationToken)
    {
        if (files.Length == 0)
        {
            throw new Cli.CommandException("give the TriG files to apply, or --from-plan.");
        }

        List<string> lines = [];
        Dictionary<RdfTerm, string> owners = [];

        foreach (string file in files)
        {
            string full = Path.GetFullPath(file);
            // An editor's byte order mark has no place in TriG.
            byte[] bytes = BaselineSource.WithoutByteOrderMark(await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false));
            InMemoryDataset declared = ManagementTrig.TryParse(bytes, new Uri(full).AbsoluteUri, out string? error)
                ?? throw new Cli.CommandException(file + ": " + error);

            if (HasDefaultGraph(declared))
            {
                throw new Cli.CommandException(file + " states a triple outside a named graph: every declaration is in a management graph (ADR 0166).");
            }

            foreach (RdfTerm graph in ManagementPlan.NamedGraphs(declared))
            {
                string name = Encoding.UTF8.GetString(graph.Lexical);

                if (!ManagementVocabulary.IsManagementGraph(graph) || graph.Equals(ManagementVocabulary.BootstrapGraph))
                {
                    throw new Cli.CommandException(file + " names <" + name + ">, which is not the fleet's graph or a dataset's (ADR 0166).");
                }

                if (!owners.TryAdd(graph, file))
                {
                    throw new Cli.CommandException("<" + name + "> is in " + owners[graph] + " and in " + file + ": one file owns a graph, whole.");
                }

                lines.AddRange(ManagementPlan.CanonicalLines(declared, graph));
            }
        }

        InMemoryDataset desired = ManagementTrig.TryParseLines(lines, out string? merged) ?? throw new Cli.CommandException(merged ?? "the declarations do not combine.");
        using HttpRequestMessage read = new(HttpMethod.Get, new Uri(manage, "graphs"));
        read.Headers.Authorization = bearer is null ? null : new AuthenticationHeaderValue("Bearer", bearer);
        using HttpResponseMessage answered = await http.SendAsync(read, cancellationToken).ConfigureAwait(false);
        byte[] body = await answered.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        if (!answered.IsSuccessStatusCode)
        {
            throw new Cli.CommandException("the server answered " + (int)answered.StatusCode + ": " + Encoding.UTF8.GetString(body));
        }

        InMemoryDataset current = ManagementTrig.TryParse(body, manage.AbsoluteUri, out string? unreadable)
            ?? throw new Cli.CommandException("the server's graphs do not parse: " + unreadable);
        long position = SparqlHttpClient.PositionOf(answered) ?? throw new Cli.CommandException("the server named no system position.");
        Dictionary<RdfTerm, ManagementPosition> versions = Versions(answered);
        return ManagementPlan.Compute(desired, owners.Keys, current, versions, new ManagementPosition(position));
    }

    private static async Task<int> CommitAsync(HttpClient http, Uri manage, string? bearer, ManagementPlan plan, Io io, CancellationToken cancellationToken)
    {
        List<string> lines = [];
        StringBuilder owned = new();

        foreach (GraphPlan graph in plan.Graphs)
        {
            lines.AddRange(graph.Content);
            owned.Append(owned.Length == 0 ? string.Empty : ", ").Append('<').Append(Encoding.UTF8.GetString(graph.Graph.Lexical)).Append(">=")
                .Append(graph.Version.Value.ToString(CultureInfo.InvariantCulture));
        }

        InMemoryDataset desired = ManagementTrig.TryParseLines(lines, out string? error) ?? throw new Cli.CommandException(error ?? "the plan does not parse.");
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(manage, "apply"))
        {
            Content = new ByteArrayContent(ManagementTrig.Write(desired, [.. plan.Graphs.Select(graph => graph.Graph)])),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/trig") { CharSet = "utf-8" };
        request.Headers.Authorization = bearer is null ? null : new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.TryAddWithoutValidation("Varve-If-Graphs-Match", owned.ToString());
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new Cli.CommandException("an owned graph moved since the plan read it, at system position " + plan.Position + ": plan again.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new Cli.CommandException("the server refused (" + (int)response.StatusCode + ", " + await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) + ")");
        }

        await io.Out.WriteLineAsync("Applied at system position " + (SparqlHttpClient.PositionOf(response)?.ToString(CultureInfo.InvariantCulture) ?? "?") + ".").ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> FromConfigAsync(string file, Io io)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(file), optional: false).Build();
        (string trig, List<string> bootstrap, List<string> errors) = LegacyConfiguration.Convert(configuration.GetSection("Varve"));

        if (errors.Count > 0)
        {
            throw new Cli.CommandException(string.Join(" ", errors));
        }

        await io.Out.WriteAsync(trig).ConfigureAwait(false);

        foreach (string principal in bootstrap)
        {
            // Not a declaration: the host's configuration, which the TriG cannot carry.
            await io.Error.WriteLineAsync("Varve:Auth:Server:Admin names " + principal + ": set it in Varve:Manage:BootstrapOperators.").ConfigureAwait(false);
        }

        return 0;
    }

    private static bool HasDefaultGraph(InMemoryDataset dataset)
    {
        using IQuadCursor cursor = dataset.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            if (cursor.Current.IsDefaultGraph)
            {
                return true;
            }
        }

        return false;
    }

    // Varve-Graph-Versions: <iri>=n, … as /manage/graphs answers it.
    private static Dictionary<RdfTerm, ManagementPosition> Versions(HttpResponseMessage response)
    {
        Dictionary<RdfTerm, ManagementPosition> versions = [];

        if (!response.Headers.TryGetValues("Varve-Graph-Versions", out IEnumerable<string>? values))
        {
            return versions;
        }

        foreach (string entry in string.Join(',', values).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = entry.LastIndexOf('=');

            if (entry.StartsWith('<') && equals > 1 && entry[equals - 1] == '>' && long.TryParse(entry.AsSpan(equals + 1), NumberStyles.None, CultureInfo.InvariantCulture, out long version))
            {
                versions[RdfTerm.Iri(Encoding.UTF8.GetBytes(entry[1..(equals - 1)]))] = new ManagementPosition(version);
            }
        }

        return versions;
    }

    // The token (ADR 0163 point 5): given; or obtained by workload identity
    // federation, the platform's own token as the client's assertion; or by
    // a client secret, which a deployment should not need.
    internal static async Task<string?> TokenAsync(
        ParseResult parsed,
        HttpClient http,
        Option<string?> token,
        Option<string?> authority,
        Option<string?> clientId,
        Option<string?> clientSecret,
        Option<string?> clientAssertion,
        Option<string?> assertionAudience,
        Option<string?> scope,
        CancellationToken cancellationToken)
    {
        if ((parsed.GetValue(token) ?? Environment.GetEnvironmentVariable("VARVE_TOKEN")) is { Length: > 0 } given)
        {
            return given;
        }

        if (parsed.GetValue(authority) is not { } issuer)
        {
            // No credential at all: a server in anonymous mode (ADR 0037).
            return parsed.GetValue(clientId) is null ? null : throw new Cli.CommandException("--authority and --client-id go together.");
        }

        string client = parsed.GetValue(clientId) ?? throw new Cli.CommandException("--authority and --client-id go together.");
        OidcClient oidc = new(http, issuer);

        if (parsed.GetValue(clientAssertion) is { } assertion)
        {
            string presented = assertion == "github"
                ? await GitHubTokenAsync(http, parsed.GetValue(assertionAudience) ?? "api://AzureADTokenExchange", cancellationToken).ConfigureAwait(false)
                : (await File.ReadAllTextAsync(assertion, cancellationToken).ConfigureAwait(false)).Trim();
            return (await oidc.ClientAssertionAsync(client, presented, parsed.GetValue(scope), cancellationToken).ConfigureAwait(false)).AccessToken;
        }

        if ((parsed.GetValue(clientSecret) ?? Environment.GetEnvironmentVariable("VARVE_CLIENT_SECRET")) is { } secret)
        {
            return (await oidc.ClientCredentialsAsync(client, secret, parsed.GetValue(scope), cancellationToken).ConfigureAwait(false)).AccessToken;
        }

        throw new Cli.CommandException("give --client-assertion (or --client-secret) with --client-id: apply runs unattended, so it has no device code flow.");
    }

    // GitHub Actions' OIDC token for the job, which needs `id-token: write`.
    private static async Task<string> GitHubTokenAsync(HttpClient http, string audience, CancellationToken cancellationToken)
    {
        string url = Environment.GetEnvironmentVariable("ACTIONS_ID_TOKEN_REQUEST_URL")
            ?? throw new Cli.CommandException("--client-assertion github runs in a GitHub Actions job with `id-token: write`: ACTIONS_ID_TOKEN_REQUEST_URL is not set.");
        string bearer = Environment.GetEnvironmentVariable("ACTIONS_ID_TOKEN_REQUEST_TOKEN") ?? throw new Cli.CommandException("ACTIONS_ID_TOKEN_REQUEST_TOKEN is not set.");
        using HttpRequestMessage request = new(HttpMethod.Get, url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "audience=" + Uri.EscapeDataString(audience));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        return body.RootElement.GetProperty("value").GetString() ?? throw new Cli.CommandException("GitHub Actions answered no token.");
    }
}
