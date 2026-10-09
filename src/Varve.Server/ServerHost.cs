// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Varve.Protocol;
using Varve.Protocol.Client;
using Varve.Protocol.Model;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>
/// The composition root (ADRs 0060, 0091, 0101): reads and validates the
/// configuration, opens the datasets, wires authentication, the clock, the
/// random source and the update executor into the protocol, serves, and on
/// SIGTERM refuses new writes, ends live feeds, drains, and closes each
/// dataset.
/// </summary>
internal static partial class ServerHost
{
    /// <summary>
    /// Runs the server. <paramref name="ready"/>, when given, is completed
    /// with the server's address once it listens: the tests' handle.
    /// </summary>
    internal static Task<int> RunAsync(string[] args, TaskCompletionSource<WebApplication>? ready) =>
        RunAsync(new ServeOptions { Arguments = args }, ready);

    /// <summary>Runs the server as <c>varve serve</c> asks (ADR 0115).</summary>
    internal static async Task<int> RunAsync(ServeOptions serve, TaskCompletionSource<WebApplication>? ready)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(serve.Arguments);

        try
        {
            AddConfigurationFiles(builder.Configuration, serve.ConfigurationFiles);
        }
        catch (FileNotFoundException missing)
        {
            await Console.Error.WriteLineAsync("varve: " + missing.Message).ConfigureAwait(false);
            return 2;
        }

        ServerSettings settings = new();
        IConfigurationSection section = builder.Configuration.GetSection("Varve");

        try
        {
            section.Bind(settings);
        }
        catch (InvalidOperationException error)
        {
            await Console.Error.WriteLineAsync("varve: the configuration does not bind: " + error.Message).ConfigureAwait(false);
            return 2;
        }

        List<string> errors = SettingsCheck.Errors(settings);

        // A key the server does not know is an error, not a silently applied
        // default (ADR 0115): a misspelt setting would otherwise be ignored.
        foreach (string unknown in SettingsCheck.UnknownKeys(section))
        {
            errors.Add(unknown + " is not a setting the server knows; see docs/operator/configure.md for every key.");
        }

        if (serve.PrintConfiguration)
        {
            await PrintConfigurationAsync(builder.Configuration, section).ConfigureAwait(false);
        }

        if (errors.Count > 0)
        {
            await Console.Error.WriteLineAsync("varve: the configuration is not valid, and the server does not start:").ConfigureAwait(false);

            foreach (string error in errors)
            {
                await Console.Error.WriteLineAsync("  - " + error).ConfigureAwait(false);
            }

            return 2;
        }

        if (serve.PrintConfiguration)
        {
            return 0;
        }

        bool anonymous = settings.Auth.Mode == "Anonymous";
        Configure(builder, settings, anonymous);
        WebApplication app = builder.Build();
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Varve.Server");

        if (anonymous)
        {
            ServerLog.AnonymousMode(logger);
        }

        TimeProvider clock = TimeProvider.System;
        await using OpenDatasets datasets = await OpenDatasets.OpenAsync(settings, clock, logger, CancellationToken.None).ConfigureAwait(false);
        IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        Drain drain = app.Services.GetRequiredService<Drain>();
        using HttpClient outbound = OutboundHttp.CreateClient();
        Map(app, settings, datasets, clock, anonymous, Outbound.Of(settings, outbound), drain, lifetime.ApplicationStopping);

        if (ready is null)
        {
            await app.RunAsync().ConfigureAwait(false);
        }
        else
        {
            await app.StartAsync().ConfigureAwait(false);
            ready.TrySetResult(app);
            await app.WaitForShutdownAsync().ConfigureAwait(false);
        }

        return 0;
    }

    // The files --config named go below the environment and the command line
    // (ADR 0115): file, then environment, then command line, later winning.
    // The default appsettings.json sources stay where the builder put them;
    // the named files are inserted before the environment's source.
    private static void AddConfigurationFiles(ConfigurationManager configuration, IReadOnlyList<string> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        int at = configuration.Sources.Count;

        for (int i = 0; i < configuration.Sources.Count; i++)
        {
            if (configuration.Sources[i] is Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource)
            {
                at = i;
                break;
            }
        }

        foreach (string file in files)
        {
            string full = Path.GetFullPath(file);
            configuration.Sources.Insert(at++, new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetDirectoryName(full)!),
                Path = Path.GetFileName(full),
                Optional = false,
                ReloadOnChange = false,
            });
        }
    }

    // The effective configuration under Varve:, each leaf with the value
    // that applies and the provider it came from, secrets redacted (ADR
    // 0115): a key whose last segment is Secret, Password, Token or Key.
    private static async Task PrintConfigurationAsync(IConfigurationRoot root, IConfigurationSection section)
    {
        ArrayBufferWriter<byte> buffer = new(1024);

        using (Utf8JsonWriter json = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartObject("Varve");
            WriteLeaves(json, root, section, string.Empty);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        await Console.Out.WriteLineAsync(System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan)).ConfigureAwait(false);
    }

    private static void WriteLeaves(Utf8JsonWriter json, IConfigurationRoot root, IConfigurationSection section, string prefix)
    {
        foreach (IConfigurationSection child in section.GetChildren())
        {
            string path = prefix.Length == 0 ? child.Key : prefix + ":" + child.Key;
            bool leaf = true;

            foreach (IConfigurationSection _ in child.GetChildren())
            {
                leaf = false;
                break;
            }

            if (!leaf)
            {
                WriteLeaves(json, root, child, path);
                continue;
            }

            json.WriteStartObject(SettingsCheck.Canonical(path) ?? path);
            json.WriteString("value", IsSecret(child.Key) ? "***" : child.Value);
            json.WriteString("from", ProviderOf(root, "Varve:" + path));
            json.WriteEndObject();
        }
    }

    private static bool IsSecret(string key) =>
        key.EndsWith("Secret", StringComparison.OrdinalIgnoreCase) || key.EndsWith("Password", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("Token", StringComparison.OrdinalIgnoreCase) || key.EndsWith("Key", StringComparison.OrdinalIgnoreCase);

    // The last provider that holds the key is the one whose value applies.
    private static string ProviderOf(IConfigurationRoot root, string key)
    {
        IConfigurationProvider[] providers = [.. root.Providers];

        for (int i = providers.Length - 1; i >= 0; i--)
        {
            if (providers[i].TryGet(key, out _))
            {
                return providers[i] switch
                {
                    Microsoft.Extensions.Configuration.CommandLine.CommandLineConfigurationProvider => "command line",
                    Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationProvider => "environment",
                    Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider json => "file " + FileOf(json.Source),
                    _ => providers[i].ToString() ?? "unknown",
                };
            }
        }

        return "default";
    }

    // The file as the operator named it, made absolute: the source keeps the
    // directory in its provider and the name in its path.
    private static string FileOf(Microsoft.Extensions.Configuration.FileConfigurationSource source) =>
        source.FileProvider is Microsoft.Extensions.FileProviders.PhysicalFileProvider physical && source.Path is not null
            ? Path.Combine(physical.Root, source.Path)
            : source.Path ?? "appsettings.json";

    private static void Configure(WebApplicationBuilder builder, ServerSettings settings, bool anonymous)
    {
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = settings.Limits.MaxRequestBody);
        IServiceCollection services = builder.Services;

        // The drain (ADR 0113): registered after the web host's own hosted
        // service, so it stops first; readiness turns false and the stop
        // waits the configured delay before the listener closes.
        services.AddSingleton(provider => new Drain(settings.Health.StopDelay, provider.GetRequiredService<IHostApplicationLifetime>()));
        services.AddHostedService(provider => provider.GetRequiredService<Drain>());

        // The health probes are unauthenticated, so they are rate-limited per
        // client address (ADR 0113); a rejection is a problem like any other.
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (rejected, cancellationToken) =>
            {
                if (rejected.HttpContext.Request.Path.StartsWithSegments("/health"))
                {
                    return new ValueTask(WriteProblemAsync(rejected.HttpContext, ProblemType.TooManyRequests, "The health endpoints answer " + settings.Health.RateLimit.ToString(CultureInfo.InvariantCulture) + " probes a minute per client address."));
                }

                rejected.HttpContext.Response.Headers.RetryAfter = "1";
                return new ValueTask(WriteProblemAsync(rejected.HttpContext, ProblemType.ServerBusy,
                    "The server has " + settings.Limits.MaxConcurrentReads.ToString(CultureInfo.InvariantCulture) + " reads in flight and " + settings.Limits.ReadQueueLength.ToString(CultureInfo.InvariantCulture) + " waiting.",
                    json => json.WriteNumber("limit", settings.Limits.MaxConcurrentReads)));
            };
            limiter.AddPolicy("health", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = settings.Health.RateLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            // The concurrent-read limit with its bounded queue (ADR 0114): a
            // read is a GET or HEAD, or a POST of application/sparql-query; a
            // write waits on the sequencer instead, and a live tail holds no
            // read slot (ADR 0095), so neither is counted.
            limiter.AddPolicy("reads", context => IsCountedRead(context)
                ? RateLimitPartition.GetConcurrencyLimiter("reads", _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = settings.Limits.MaxConcurrentReads,
                    QueueLimit = settings.Limits.ReadQueueLength,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                })
                : RateLimitPartition.GetNoLimiter("unbounded"));
        });

        if (settings.ForwardedHeaders.Enabled)
        {
            services.Configure<ForwardedHeadersOptions>(forwarded =>
            {
                forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                forwarded.KnownProxies.Clear();
                forwarded.KnownIPNetworks.Clear();

                foreach (string proxy in settings.ForwardedHeaders.KnownProxies)
                {
                    forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
                }
            });
        }

        if (anonymous)
        {
            services.AddAuthorization(authorization =>
            {
                foreach (string permission in (ReadOnlySpan<string>)[DatasetPermissions.Read, DatasetPermissions.Write, DatasetPermissions.Admin, DatasetPermissions.ServerAdmin])
                {
                    authorization.AddPolicy(permission, policy => policy.RequireAssertion(_ => true));
                }
            });
            return;
        }

        AuthSettings auth = settings.Auth;
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(bearer =>
        {
            bearer.Authority = auth.Authority;
            bearer.RequireHttpsMetadata = auth.RequireHttpsMetadata;
            bearer.MapInboundClaims = false;

            // A refusal says nothing about why: no error description in
            // WWW-Authenticate, so a probe learns nothing (7a's auth tests).
            // RFC 6750 §3's error codes are set here (ADR 0119): invalid_token
            // when a token was presented and failed, none when none was, and
            // insufficient_scope on a forbid; the bodies are the thin
            // unauthorized problem and the forbidden problem.
            bearer.IncludeErrorDetails = false;
            bearer.Events = new JwtBearerEvents
            {
                OnChallenge = async challenge =>
                {
                    challenge.HandleResponse();
                    challenge.Response.Headers.WWWAuthenticate = challenge.AuthenticateFailure is not null ? "Bearer error=\"invalid_token\"" : "Bearer";
                    await WriteProblemAsync(challenge.HttpContext, ProblemType.Unauthorized, null).ConfigureAwait(false);
                },
                OnForbidden = forbidden =>
                {
                    forbidden.Response.Headers.WWWAuthenticate = "Bearer error=\"insufficient_scope\"";
                    return WriteProblemAsync(forbidden.HttpContext, ProblemType.Forbidden, null);
                },
            };
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidAudiences = auth.Audiences,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                NameClaimType = auth.SubjectClaim,
                RoleClaimType = auth.RoleClaimType,
            };
        });
        services.AddSingleton<IAuthorizationHandler>(new DatasetPermissionHandler(auth));
        services.AddAuthorization(authorization =>
        {
            authorization.AddPolicy(DatasetPermissions.Read, policy => policy.AddRequirements(new DatasetPermission(Permission.Read)));
            authorization.AddPolicy(DatasetPermissions.Write, policy => policy.AddRequirements(new DatasetPermission(Permission.Write)));
            authorization.AddPolicy(DatasetPermissions.Admin, policy => policy.AddRequirements(new DatasetPermission(Permission.Admin)));
            authorization.AddPolicy(DatasetPermissions.ServerAdmin, policy => policy.AddRequirements(new DatasetPermission(Permission.ServerAdmin)));
        });
    }

    private static void Map(WebApplication app, ServerSettings settings, OpenDatasets datasets, TimeProvider clock, bool anonymous, Outbound outbound, Drain drain, CancellationToken stopping)
    {
        if (settings.ForwardedHeaders.Enabled)
        {
            app.UseForwardedHeaders();
        }

        app.UseRateLimiter();

        // On SIGTERM: a new write is refused, while what is in flight drains
        // (ADR 0101). Reads still answer until the host stops listening.
        app.Use(async (context, next) =>
        {
            if (stopping.IsCancellationRequested && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                await WriteProblemAsync(context, ProblemType.ShuttingDown, null).ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        // Every non-2xx is a problem (ADR 0119): a route nobody serves, and a
        // method the router refused, which it answers with an empty body.
        app.UseStatusCodePages(async statusCode =>
        {
            HttpContext context = statusCode.HttpContext;

            switch (context.Response.StatusCode)
            {
                case StatusCodes.Status404NotFound:
                    await WriteProblemAsync(context, ProblemType.NotFound, null).ConfigureAwait(false);
                    break;
                case StatusCodes.Status405MethodNotAllowed:
                    await WriteProblemAsync(context, ProblemType.MethodNotAllowed, null).ConfigureAwait(false);
                    break;
                case StatusCodes.Status415UnsupportedMediaType:
                    await WriteProblemAsync(context, ProblemType.UnsupportedMediaType, null).ConfigureAwait(false);
                    break;
                default:
                    break;
            }
        });

        if (!anonymous)
        {
            app.UseAuthentication();
        }

        ProtocolOptions options = new()
        {
            Datasets = datasets,
            Administration = datasets,
            Updates = new StoreUpdates(clock, outbound, new MemoryBytes(settings.Limits.MaxQueryMemory)),
            Identity = anonymous ? new NoAgent() : new TokenIdentity(settings.Auth.SubjectClaim),
            AccessScopes = anonymous ? EveryoneEverything.Instance : new GrantedScopes(settings.Auth),
            Authorization = app.Services.GetRequiredService<IAuthorizationService>(),
            Clock = clock,
            Limits = Limits(settings),
            Evaluation = new EvaluationOptions { Clock = clock, Randomness = new SystemRandomness(), ServiceHandler = outbound.Service, MemoryBudget = new MemoryBytes(settings.Limits.MaxQueryMemory) },
            Stopping = stopping,
        };

        app.MapGroup("/datasets/{dataset}").RequireRateLimiting("reads").MapVarveDataset(options);
        app.MapVarveAdministration("/datasets", options);
        app.MapGet("/health/live", (RequestDelegate)(context => WriteJsonAsync(context, StatusCodes.Status200OK, "application/json", json => json.WriteString("status", "live")))).RequireRateLimiting("health");
        app.MapGet("/health/ready", (RequestDelegate)(context => ReadyAsync(context, datasets, settings.Health.ReadyLag, drain))).RequireRateLimiting("health");
    }

    // Ready when every dataset that should be open is open, no default
    // projection is failed or behind the allowed lag, and the host is not
    // draining (ADRs 0101, 0106, 0113). Datasets are opened and their
    // projections replayed before the server listens, so a failure to open
    // — a directory under the root that is leased elsewhere, or does not
    // parse — a projection failure and the drain are what remain to report.
    // A dataset closed by the admin API is reported and does not count.
    private static Task ReadyAsync(HttpContext context, OpenDatasets datasets, long allowedLag, Drain drain)
    {
        List<DatasetHealth> health = [];

        foreach (DatasetEntry entry in datasets.List())
        {
            Dataset? dataset = null;
            bool open = entry.State == DatasetState.Open && datasets.TryResolve(entry.Name, out dataset);
            health.Add(new DatasetHealth(
                entry.Name.Value,
                entry.State,
                entry.Reason,
                open ? dataset!.Head.Value : 0,
                open ? dataset!.ProjectionPosition.Value : 0,
                open && dataset!.IsFailed));
        }

        (bool ready, List<DatasetReadiness> states) = Readiness.Evaluate(health, allowedLag, drain.Draining);
        context.Response.Headers.CacheControl = "no-store";

        if (ready)
        {
            return WriteJsonAsync(context, StatusCodes.Status200OK, "application/json", json =>
            {
                json.WriteString("status", "ready");
                json.WriteStartObject("datasets");

                foreach (DatasetReadiness state in states)
                {
                    json.WriteString(state.Name, state.State);
                }

                json.WriteEndObject();
            });
        }

        return WriteProblemAsync(context, ProblemType.NotReady, drain.Draining ? "The server is draining." : null, json =>
        {
            json.WriteStartArray("datasets");

            foreach (DatasetReadiness state in states)
            {
                if (state.State is "ready" or "closed")
                {
                    continue;
                }

                json.WriteStartObject();
                json.WriteString("name", state.Name);
                json.WriteString("state", state.State);

                if (state.Reason is not null)
                {
                    json.WriteString("reason", state.Reason);
                }

                if (state.Lag is long lag)
                {
                    json.WriteNumber("lag", lag);
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
        });
    }

    /// <summary>
    /// A problem the host writes outside the protocol's endpoints (ADR 0119),
    /// from the catalogue: the request id as instance, except on the
    /// deliberately thin <c>401</c>.
    /// </summary>
    private static async Task WriteProblemAsync(HttpContext context, ProblemType type, string? detail, Action<Utf8JsonWriter>? members = null)
    {
        ArrayBufferWriter<byte> body = new(256);

        using (Utf8JsonWriter json = new(body))
        {
            ProblemCatalogue.Write(json, type, type == ProblemType.Unauthorized ? null : context.TraceIdentifier, detail, members);
        }

        context.Response.StatusCode = ProblemCatalogue.Of(type).Status;
        context.Response.ContentType = "application/problem+json";
        context.Response.ContentLength = body.WrittenCount;
        await context.Response.Body.WriteAsync(body.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync(HttpContext context, int status, string contentType, Action<Utf8JsonWriter> write)
    {
        ArrayBufferWriter<byte> body = new(256);

        using (Utf8JsonWriter json = new(body))
        {
            json.WriteStartObject();
            write(json);
            json.WriteEndObject();
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        context.Response.ContentLength = body.WrittenCount;
        await context.Response.Body.WriteAsync(body.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>The protocol's limits from the configuration (ADRs 0095, 0114).</summary>
    private static ProtocolLimits Limits(ServerSettings settings) => new(
        settings.Limits.QueryTimeout,
        settings.Limits.PinnedReadLifetime,
        new ByteCount(settings.Limits.ResultSizeCap),
        new ByteCount(settings.Limits.MaxRequestBody),
        settings.Limits.FeedHeartbeat)
    {
        MaxQueryMemory = new MemoryBytes(settings.Limits.MaxQueryMemory),
        MaxAsOfDistance = settings.Limits.MaxAsOfDistance,
        MaxLiveTailsPerClient = settings.Limits.MaxLiveTailsPerClient,
        CommitsPageSize = settings.Limits.CommitsPageSize,
    };

    // A read for the concurrency limit (ADR 0114): a GET or HEAD that is not
    // an open commits range, or a POSTed query.
    private static bool IsCountedRead(HttpContext context)
    {
        HttpRequest request = context.Request;

        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            bool commits = request.Path.Value?.EndsWith("/commits", StringComparison.Ordinal) == true;
            return !commits || request.Query.ContainsKey("to") || request.Query.ContainsKey("toTime");
        }

        return HttpMethods.IsPost(request.Method) && request.ContentType?.StartsWith("application/sparql-query", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>The update executor (ADR 0091): Varve.Sparql.Store, no retries, the expected position passed through, SERVICE and LOAD through the outbound client (ADR 0104), the memory budget of ADR 0114.</summary>
    private sealed class StoreUpdates(TimeProvider clock, Outbound outbound, MemoryBytes budget) : ISparqlUpdateExecutor
    {
        public ValueTask<CommitResult> ExecuteAsync(Dataset dataset, Update update, CommitMetadata metadata, Position? expectedPosition, Varve.Rdf.CallerScope scope, CancellationToken cancellationToken) =>
            SparqlUpdate.ExecuteAsync(dataset, update, new UpdateOptions
            {
                Metadata = metadata,
                ExpectedPosition = expectedPosition,
                Evaluation = new EvaluationOptions { Clock = clock, Randomness = new SystemRandomness(), ServiceHandler = outbound.Service, MemoryBudget = budget },
                LoadSource = outbound.Load,
                ReadScope = scope.Readable,
                WriteScope = scope.Writable,
            }, cancellationToken);
    }

    /// <summary>RAND(), UUID() and STRUUID() at the composition root: the operating system's (ADR 0056).</summary>
    private sealed class SystemRandomness : IRandomSource
    {
        public void NextBytes(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
    }

    private static partial class ServerLog
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
            Message = "Anonymous mode is on: every caller may read, write and administer every dataset, and commits name no agent. It is for development only (ADR 0037).")]
        internal static partial void AnonymousMode(ILogger logger);
    }
}
