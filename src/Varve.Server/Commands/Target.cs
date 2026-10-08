// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server.Commands;

/// <summary>
/// What a command's dataset argument resolves to (ADR 0105): a directory,
/// opened with the file storage and no authentication, or a URL, reached
/// through the client with the token the options obtain.
/// </summary>
internal abstract class Opened : IAsyncDisposable
{
    public abstract ValueTask DisposeAsync();
}

/// <summary>A dataset directory, open for the command's lifetime.</summary>
internal sealed class Local(Dataset dataset, FileStorage files, Outbound outbound) : Opened
{
    internal Dataset Dataset { get; } = dataset;

    internal Outbound Outbound { get; } = outbound;

    internal EvaluationOptions Evaluation => new() { Clock = TimeProvider.System, Randomness = new SystemRandomness(), ServiceHandler = Outbound.Service };

    internal UpdateOptions UpdateOptions(Position? expected) => new()
    {
        Evaluation = Evaluation,
        LoadSource = Outbound.Load,
        ExpectedPosition = expected,
    };

    public override async ValueTask DisposeAsync()
    {
        await Dataset.DisposeAsync().ConfigureAwait(false);
        await files.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>RAND(), UUID() and STRUUID() at the composition root: the operating system's (ADR 0056).</summary>
    private sealed class SystemRandomness : IRandomSource
    {
        public void NextBytes(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
    }
}

/// <summary>A dataset on a server.</summary>
internal sealed class Remote(SparqlHttpClient client, HttpClient http) : Opened
{
    internal SparqlHttpClient Client { get; } = client;

    public override ValueTask DisposeAsync()
    {
        http.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>The options every command over a dataset shares, and how they open it.</summary>
internal sealed class Target
{
    internal readonly Argument<string> Dataset = new("dataset") { Description = "A dataset directory, or the URL of a dataset on a server (…/datasets/{name}/)." };
    internal readonly Option<string?> Authority = new("--authority") { Description = "The OIDC issuer a server's tokens come from, for a URL." };
    internal readonly Option<string?> ClientId = new("--client-id") { Description = "The OIDC client: with --client-secret the client credentials flow, alone the device code flow." };
    internal readonly Option<string?> ClientSecret = new("--client-secret") { Description = "The client's secret; VARVE_CLIENT_SECRET when absent." };
    internal readonly Option<string?> Scope = new("--scope") { Description = "The scope to ask the issuer for." };
    internal readonly Option<string?> Token = new("--token") { Description = "A bearer token obtained elsewhere; VARVE_TOKEN when absent. Nothing is stored." };
    internal readonly Option<bool> NoStore = new("--no-store") { Description = "Keep no refresh token on disk: for CI and shared machines." };
    internal readonly Option<string?> Credentials = new("--credentials") { Description = "The credential file; by default under the user's profile." };
    internal readonly Option<string[]> AllowEndpoint = new("--allow-endpoint") { Description = "An IRI prefix SERVICE may reach from a local dataset; none by default. Repeatable." };
    internal readonly Option<string[]> AllowSource = new("--allow-source") { Description = "An IRI prefix LOAD may fetch from for a local dataset; none by default. Repeatable." };
    internal readonly Option<bool> AllowPrivate = new("--allow-private-addresses") { Description = "Let SERVICE and LOAD reach loopback and private addresses." };

    internal void AddTo(Command command, bool remote = true)
    {
        command.Arguments.Add(Dataset);
        command.Options.Add(AllowEndpoint);
        command.Options.Add(AllowSource);
        command.Options.Add(AllowPrivate);

        if (remote)
        {
            command.Options.Add(Authority);
            command.Options.Add(ClientId);
            command.Options.Add(ClientSecret);
            command.Options.Add(Scope);
            command.Options.Add(Token);
            command.Options.Add(NoStore);
            command.Options.Add(Credentials);
        }
    }

    internal static bool IsUrl(string dataset) =>
        dataset.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || dataset.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Opens the dataset the parse result names.</summary>
    internal async Task<Opened> OpenAsync(ParseResult parsed, Io io, CancellationToken cancellationToken)
    {
        string dataset = parsed.GetValue(Dataset)!;

        if (IsUrl(dataset))
        {
            return await OpenRemoteAsync(parsed, io, new Uri(dataset, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
        }

        return await OpenLocalAsync(dataset, parsed, mustExist: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens a directory as a dataset; a missing one is an error unless <paramref name="mustExist"/> is off, when it is created.</summary>
    internal async Task<Local> OpenLocalAsync(string directory, ParseResult parsed, bool mustExist, CancellationToken cancellationToken)
    {
        bool exists = Directory.Exists(Path.Combine(directory, "log"));

        if (mustExist && !exists)
        {
            throw new Cli.CommandException(directory + " holds no dataset: no log/ directory. Create one with `varve create`.");
        }

        TimeProvider clock = TimeProvider.System;
        FileStorage files = await FileStorage.OpenAsync(new DatasetDirectory(Path.GetFullPath(directory)), new FileStorageOptions { Clock = clock }, cancellationToken).ConfigureAwait(false);

        try
        {
            DatasetOptions options = new() { Clock = clock };
            Dataset dataset = exists
                ? await Varve.Store.Dataset.OpenAsync(files, options, cancellationToken).ConfigureAwait(false)
                : await Varve.Store.Dataset.CreateAsync(files, new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false);
            return new Local(dataset, files, OutboundOf(parsed));
        }
        catch
        {
            await files.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private Outbound OutboundOf(ParseResult parsed)
    {
        string[] endpoints = parsed.GetValue(AllowEndpoint) ?? [];
        string[] sources = parsed.GetValue(AllowSource) ?? [];
        bool allowPrivate = parsed.GetValue(AllowPrivate);
        ServerSettings settings = new();
        settings.Federation.AllowedEndpoints.AddRange(endpoints);
        settings.Federation.AllowPrivateAddresses = allowPrivate;
        settings.Load.AllowedSources.AddRange(sources);
        settings.Load.AllowPrivateAddresses = allowPrivate;
        return Outbound.Of(settings, OutboundHttp.CreateClient());
    }

    private async Task<Remote> OpenRemoteAsync(ParseResult parsed, Io io, Uri dataset, CancellationToken cancellationToken)
    {
        HttpClient http = OutboundHttp.CreateClient();

        try
        {
            SparqlHttpClient client = new(http, dataset, ClientLimits.Default)
            {
                AccessToken = await TokenAsync(parsed, io, dataset, http, cancellationToken).ConfigureAwait(false),
            };
            return new Remote(client, http);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    // The token: given, or obtained from the issuer by the flow the options
    // name, a stored refresh token first (ADR 0105).
    private async Task<string?> TokenAsync(ParseResult parsed, Io io, Uri dataset, HttpClient http, CancellationToken cancellationToken)
    {
        string? token = parsed.GetValue(Token) ?? Environment.GetEnvironmentVariable("VARVE_TOKEN");

        if (!string.IsNullOrEmpty(token))
        {
            return token;
        }

        string? authority = parsed.GetValue(Authority);
        string? clientId = parsed.GetValue(ClientId);

        if (authority is null && clientId is null)
        {
            return null;
        }

        if (authority is null || clientId is null)
        {
            throw new Cli.CommandException("--authority and --client-id go together.");
        }

        string? secret = parsed.GetValue(ClientSecret) ?? Environment.GetEnvironmentVariable("VARVE_CLIENT_SECRET");
        string? scope = parsed.GetValue(Scope);
        bool store = !parsed.GetValue(NoStore);
        CredentialFile credentials = new(parsed.GetValue(Credentials));
        OidcClient oidc = new(http, authority);

        if (store && credentials.TryRead(dataset, authority, clientId) is { } refresh)
        {
            try
            {
                OidcTokens refreshed = await oidc.RefreshAsync(clientId, secret, refresh, cancellationToken).ConfigureAwait(false);
                credentials.Write(dataset, authority, clientId, refreshed.RefreshToken ?? refresh);
                return refreshed.AccessToken;
            }
            catch (OidcException)
            {
                // A revoked or expired refresh token: authenticate again.
                credentials.Remove(dataset, authority, clientId);
            }
        }

        OidcTokens tokens = secret is not null
            ? await oidc.ClientCredentialsAsync(clientId, secret, scope, cancellationToken).ConfigureAwait(false)
            : await oidc.DeviceCodeAsync(clientId, scope, io.Out, cancellationToken).ConfigureAwait(false);

        if (store && tokens.RefreshToken is { } kept)
        {
            credentials.Write(dataset, authority, clientId, kept);
        }

        return tokens.AccessToken;
    }
}
