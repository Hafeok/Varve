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
using Varve.Protocol;
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

/// <summary>
/// A dataset opened in this process for the command's lifetime: a directory,
/// or a dataset in an object store opened read-only (ADR 0160).
/// </summary>
internal sealed class Local(Dataset dataset, IAsyncDisposable files, Outbound outbound, string? cache = null) : Opened
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

        if (cache is not null && Directory.Exists(cache))
        {
            Directory.Delete(cache, recursive: true);
        }
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
    internal readonly Argument<string> Dataset = new("dataset")
    {
        Description = "A dataset directory; the URL of a dataset on a server (…/datasets/{name}/); or a dataset in an object store, read-only: https://{account}.blob.core.windows.net/{container}/{dataset} or s3://{bucket}/{dataset}.",
    };

    internal readonly Option<bool> Unannounced = new("--unannounced")
    {
        Description = "Object store: read without a reader record, for an identity that may only read; the writer may then delete what the read holds, and such a read fails rather than answer wrong (ADR 0160).",
    };

    internal readonly Option<string?> Endpoint = new("--endpoint") { Description = "s3://: the S3-compatible service's endpoint." };
    internal readonly Option<string?> Region = new("--region") { Description = "s3://: the region the signature is for." };
    internal readonly Option<string?> StsEndpoint = new("--sts-endpoint") { Description = "s3://: the STS endpoint web identity is exchanged at, a self-hosted server's own; Amazon's when absent." };
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
        command.Options.Add(Unannounced);
        command.Options.Add(Endpoint);
        command.Options.Add(Region);
        command.Options.Add(StsEndpoint);
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

        if (ObjectLease.IsObjectStore(dataset))
        {
            return await OpenObjectStoreAsync(dataset, parsed, cancellationToken).ConfigureAwait(false);
        }

        if (parsed.GetValue(Unannounced))
        {
            throw new Cli.CommandException("--unannounced is for a dataset in an object store (ADR 0160).");
        }

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
            DatasetOptions options = new() { Clock = clock, Validators = [new EventCatalogueValidator(), new ReportGraphGuard()] };
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

    /// <summary>
    /// A dataset in an object store, opened read-only (ADRs 0139, 0160): it
    /// claims nothing, and announces the catalog version it reads in a reader
    /// record of a fresh id, deleted when the command ends — unless
    /// <c>--unannounced</c> asks, explicitly, for no record. Writes are refused.
    /// </summary>
    [DecisionDriven.DesignDecision(typeof(DecisionDriven.Ledger.Varve.StorageOperationsAndTheReadersCommand.ReadCommandsMayReadUnannounced), Scope = DecisionDriven.ExceptionScope.Boundary)]
    private async Task<Local> OpenObjectStoreAsync(string address, ParseResult parsed, CancellationToken cancellationToken)
    {
        (IObjectStore store, ObjectName prefix) = ObjectLease.Open(address, parsed.GetValue(Endpoint), parsed.GetValue(Region), parsed.GetValue(StsEndpoint));
        string cache = Directory.CreateTempSubdirectory("varve-read-").FullName;
        TimeProvider clock = TimeProvider.System;
        Varve.Store.ObjectStorage.ObjectStorage? storage = null;

        try
        {
            storage = await Varve.Store.ObjectStorage.ObjectStorage.OpenAsync(new Varve.Store.ObjectStorage.Model.ObjectStorageOptions
            {
                Store = store,
                Prefix = prefix,
                Cache = new Varve.Store.ObjectStorage.Model.CacheDirectory(cache),
                Access = Varve.Store.ObjectStorage.Model.ObjectStorageAccess.ReadOnly,
                Announcing = parsed.GetValue(Unannounced) ? Varve.Store.ObjectStorage.Model.ReaderAnnouncing.Unannounced : Varve.Store.ObjectStorage.Model.ReaderAnnouncing.Required,
                ProbeRandomness = SystemProbeRandomness.Instance,
                Clock = clock,
            }, cancellationToken).ConfigureAwait(false);
            DatasetOptions options = new() { Clock = clock, Validators = [new EventCatalogueValidator(), new ReportGraphGuard()], Maintenance = MaintenanceMode.Off, ExtentMerges = ExtentMergePolicy.Off };
            Dataset dataset = await Varve.Store.Dataset.OpenAsync(storage, options, cancellationToken).ConfigureAwait(false);
            return new Local(dataset, new OwnedStore(storage, store), OutboundOf(parsed), cache);
        }
        catch
        {
            if (storage is not null)
            {
                await storage.DisposeAsync().ConfigureAwait(false);
            }

            (store as IDisposable)?.Dispose();
            Directory.Delete(cache, recursive: true);
            throw;
        }
    }

    // The backend, then the adapter it was opened over.
    private sealed class OwnedStore(Varve.Store.ObjectStorage.ObjectStorage storage, IObjectStore store) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await storage.DisposeAsync().ConfigureAwait(false);
            (store as IDisposable)?.Dispose();
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

    internal async Task<Remote> OpenRemoteAsync(ParseResult parsed, Io io, Uri dataset, CancellationToken cancellationToken)
    {
        HttpClient http = OutboundHttp.CreateClient();

        try
        {
            BearerTokenProvider? tokens = await TokensAsync(parsed, io, dataset, http, cancellationToken).ConfigureAwait(false);
            SparqlHttpClient client = tokens is null
                ? new(http, dataset, ClientLimits.Default)
                : new(http, dataset, ClientLimits.Default, tokens, TimeProvider.System);
            return new Remote(client, http);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    // The tokens (ADRs 0105, 0127): a token given is fixed; one obtained from
    // the issuer by the flow the options name, a stored refresh token first,
    // is renewed before it expires, so a long command outlives one token.
    private async Task<BearerTokenProvider?> TokensAsync(ParseResult parsed, Io io, Uri dataset, HttpClient http, CancellationToken cancellationToken)
    {
        string? token = parsed.GetValue(Token) ?? Environment.GetEnvironmentVariable("VARVE_TOKEN");

        if (!string.IsNullOrEmpty(token))
        {
            return Varve.Protocol.Client.Model.BearerToken.Fixed(token);
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
                return new RenewedTokens(oidc, clientId, secret, scope, store ? Kept(credentials, dataset, authority, clientId) : null, refreshed with { RefreshToken = refreshed.RefreshToken ?? refresh }).GetAsync;
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

        return new RenewedTokens(oidc, clientId, secret, scope, store ? Kept(credentials, dataset, authority, clientId) : null, tokens).GetAsync;
    }

    private static Action<string> Kept(CredentialFile credentials, Uri dataset, string authority, string clientId) =>
        refresh => credentials.Write(dataset, authority, clientId, refresh);
}
