// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
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
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Varve.Events.Model;
using Varve.Protocol;
using Varve.Protocol.Client;
using Varve.Protocol.Model;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>A server that listens: its host, and its part in the fleet; the tests' handle.</summary>
internal sealed record StartedServer(WebApplication App, Fleet Fleet);

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
    internal static Task<int> RunAsync(string[] args, TaskCompletionSource<StartedServer>? ready) =>
        RunAsync(new ServeOptions { Arguments = args }, ready);

    /// <summary>Runs the server as <c>varve serve</c> asks (ADR 0115).</summary>
    internal static async Task<int> RunAsync(ServeOptions serve, TaskCompletionSource<StartedServer>? ready)
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
            errors.Add(SettingsCheck.UnknownKeyError(unknown));
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
        HttpContextAccessor accessor = new();
        FleetHandle handle = new();
        Configure(builder, settings, anonymous, accessor, handle);
        WebApplication app = builder.Build();
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Varve.Server");

        if (anonymous)
        {
            ServerLog.AnonymousMode(logger);
        }

        TimeProvider clock = TimeProvider.System;
        Fleet fleet;

        try
        {
            fleet = await Fleet.StartAsync(settings, serve.RecordBootstrap, clock, logger, accessor, CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException refused)
        {
            await Console.Error.WriteLineAsync("varve: " + refused.Message).ConfigureAwait(false);
            return 2;
        }

        await using (fleet.ConfigureAwait(false))
        {
            handle.Fleet = fleet;
            IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            Drain drain = app.Services.GetRequiredService<Drain>();
            using HttpClient outbound = OutboundHttp.CreateClient();
            using HttpMessageInvoker forwarder = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }, disposeHandler: true);
            Map(app, settings, fleet, clock, anonymous, Outbound.Of(settings, outbound), forwarder, drain, lifetime.ApplicationStopping);
            Task following = fleet.RunAsync(lifetime.ApplicationStopping);

            if (ready is null)
            {
                await app.RunAsync().ConfigureAwait(false);
            }
            else
            {
                await app.StartAsync().ConfigureAwait(false);
                ready.TrySetResult(new StartedServer(app, fleet));
                await app.WaitForShutdownAsync().ConfigureAwait(false);
            }

            await following.ConfigureAwait(false);
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

    private static void Configure(WebApplicationBuilder builder, ServerSettings settings, bool anonymous, HttpContextAccessor accessor, FleetHandle handle)
    {
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = settings.Limits.MaxRequestBody);

        // The management plane's own listener (ADR 0163 point 1), beside the
        // host's: the listeners are the host's URLs and these.
        if (ManagementPorts(settings) is { Count: > 0 })
        {
            string listening = builder.Configuration["urls"] ?? builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5000";
            builder.WebHost.UseUrls([.. listening.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), .. settings.Manage.Urls!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]);
        }

        IServiceCollection services = builder.Services;
        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddSingleton(handle);
        Telemetry.Configure(builder);

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

        if (settings.Cors.AllowedOrigins.Count > 0)
        {
            services.AddCors(cors => cors.AddDefaultPolicy(policy => Cors(policy, settings.Cors)));
        }

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

        // A scheme per plane (ADR 0163 point 1), chosen by the path: each
        // accepts only its own audience, and a token for both is refused by both.
        AuthSettings auth = settings.Auth;
        string[] data = [.. auth.Audiences];
        string[] management = [auth.Management.Audience];
        services.AddAuthentication(PlaneScheme)
            .AddPolicyScheme(PlaneScheme, null, plane => plane.ForwardDefaultSelector = context => IsManagement(context.Request.Path) ? ManagementScheme : DataScheme)
            .AddJwtBearer(DataScheme, bearer => Bearer(bearer, auth, data, management))
            .AddJwtBearer(ManagementScheme, bearer => Bearer(bearer, auth, management, data));
        services.AddSingleton<IAuthorizationHandler>(new DatasetPermissionHandler(handle, auth, accessor));
        services.AddAuthorization(authorization =>
        {
            authorization.AddPolicy(DatasetPermissions.Read, policy => policy.AddRequirements(new DatasetPermission(Permission.Read)));
            authorization.AddPolicy(DatasetPermissions.Write, policy => policy.AddRequirements(new DatasetPermission(Permission.Write)));
            authorization.AddPolicy(DatasetPermissions.Admin, policy => policy.AddRequirements(new DatasetPermission(Permission.Admin)));
            authorization.AddPolicy(DatasetPermissions.ServerAdmin, policy => policy.AddRequirements(new DatasetPermission(Permission.ServerAdmin)));
        });
    }

    private const string PlaneScheme = "varve";
    private const string DataScheme = "data";
    private const string ManagementScheme = "manage";

    // The ports of Varve:Manage:Urls; none when it is unset.
    private static HashSet<int> ManagementPorts(ServerSettings settings)
    {
        HashSet<int> ports = [];

        foreach (string url in (settings.Manage.Urls ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Uri.TryCreate(url.Replace("*", "localhost", StringComparison.Ordinal).Replace("+", "localhost", StringComparison.Ordinal), UriKind.Absolute, out Uri? parsed))
            {
                ports.Add(parsed.Port);
            }
        }

        return ports;
    }

    /// <summary>Whether a path is the management plane's.</summary>
    internal static bool IsManagement(PathString path) => path.StartsWithSegments("/manage");

    private static void Bearer(JwtBearerOptions bearer, AuthSettings auth, string[] own, string[] other)
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
            ValidAudiences = own,
            AudienceValidator = (audiences, _, _) => IsPlaneAudience(audiences, own, other),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            NameClaimType = auth.SubjectClaim,
            RoleClaimType = auth.RoleClaimType,
        };
    }

    /// <summary>A token's audiences name this plane's and none of the other's (ADR 0163 point 1).</summary>
    internal static bool IsPlaneAudience(IEnumerable<string> audiences, string[] own, string[] other)
    {
        bool mine = false;

        foreach (string audience in audiences)
        {
            if (Array.IndexOf(other, audience) >= 0)
            {
                return false;
            }

            mine |= Array.IndexOf(own, audience) >= 0;
        }

        return mine;
    }

    // The one policy (ADR 0132): the configured origins; the methods the API
    // serves; the request headers it reads and the response headers a client
    // needs, each named since CORS has no prefix wildcard; never credentials.
    private static void Cors(CorsPolicyBuilder policy, CorsSettings settings)
    {
        if (settings.AllowedOrigins.Contains("*"))
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins([.. settings.AllowedOrigins]);
        }

        policy
            .WithMethods("GET", "HEAD", "POST", "PUT", "PATCH", "DELETE")
            .WithHeaders([.. CorsRequestHeaders])
            .WithExposedHeaders([.. CorsResponseHeaders])
            .SetPreflightMaxAge(settings.MaxAge)
            .DisallowCredentials();
    }

    /// <summary>The request headers a browser may send (ADR 0132).</summary>
    internal static ImmutableArray<string> CorsRequestHeaders { get; } =
    [
        "Authorization", "Content-Type", "Accept", "If-Match", "If-None-Match", "Varve-If-Graphs-Match", "Last-Event-ID", "Varve-As-Of", "Varve-Min-Position", "Varve-Min-Management-Position", "traceparent", "tracestate",
    ];

    /// <summary>The response headers a browser may read beyond the safelisted ones (ADR 0132).</summary>
    internal static ImmutableArray<string> CorsResponseHeaders { get; } =
    [
        "Varve-Position", "Varve-Management-Position", "Varve-Graph-Versions", "ETag", "Varve-Request-Id", "Link", "Location", "Retry-After", "WWW-Authenticate",
    ];

    private static void Map(WebApplication app, ServerSettings settings, Fleet fleet, TimeProvider clock, bool anonymous, Outbound outbound, HttpMessageInvoker forwarder, Drain drain, CancellationToken stopping)
    {
        // With a management listener, the management plane is served on it
        // alone, and nothing else is (ADR 0163 point 1); health answers on both.
        HashSet<int> managementPorts = ManagementPorts(settings);

        if (managementPorts.Count > 0)
        {
            app.Use((context, next) =>
            {
                bool management = IsManagement(context.Request.Path);
                bool health = context.Request.Path.StartsWithSegments("/health");

                if (!health && management != managementPorts.Contains(context.Connection.LocalPort))
                {
                    return WriteProblemAsync(context, ProblemType.NotFound, null);
                }

                return next(context);
            });
        }

        // Request ids are unique across the fleet (ADR 0168 point 7).
        string instance = fleet.Instance.Value;
        app.Use((context, next) =>
        {
            context.TraceIdentifier = instance + ":" + context.TraceIdentifier;
            return next(context);
        });

        if (settings.ForwardedHeaders.Enabled)
        {
            app.UseForwardedHeaders();
        }

        // CORS first (ADR 0132): a preflight is answered before a token or a
        // rate limit is asked for, and every response after it, a problem
        // included, carries the headers a browser needs to show it. With no
        // origin configured the middleware is not there at all.
        if (settings.Cors.AllowedOrigins.Count > 0)
        {
            app.UseCors();
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

        // The grants a request is decided by are pinned at its start, at the
        // position applied then; a request may ask for a later one (ADR 0163
        // point 3), and every response names the position it was served at.
        app.Use(ManagementPositionAsync(fleet));

        OpenDatasets datasets = fleet.Datasets;
        StoreUpdates updates = new(clock, outbound, new MemoryBytes(settings.Limits.MaxQueryMemory));
        IAccessScopes scopes = anonymous ? EveryoneEverything.Instance : new ManagedScopes(app.Services.GetRequiredService<FleetHandle>(), settings.Auth, app.Services.GetRequiredService<IHttpContextAccessor>());
        ProtocolOptions options = new()
        {
            Datasets = datasets,
            Administration = datasets,
            Updates = updates,
            Writers = fleet.IsFleetOfOne ? null : fleet,
            Forwarder = forwarder,
            Management = fleet,
            Following = fleet,
            Identity = anonymous ? new NoAgent() : new TokenIdentity(settings.Auth.SubjectClaim, settings.Auth.ApplicationClaim),
            AccessScopes = scopes,
            Authorization = app.Services.GetRequiredService<IAuthorizationService>(),
            Clock = clock,
            Limits = Limits(settings),
            Evaluation = new EvaluationOptions { Clock = clock, Randomness = new SystemRandomness(), ServiceHandler = outbound.Service, MemoryBudget = new MemoryBytes(settings.Limits.MaxQueryMemory) },
            Stopping = stopping,
            Telemetry = new TelemetryOptions { QueryText = settings.Telemetry.QueryText },
            Logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Varve.Protocol"),
            EventSource = settings.Events.Source,
            EventHubs = string.IsNullOrEmpty(settings.Events.EventHubs.Host) ? null : new EventHubsServer(settings.Events.EventHubs.Host, settings.Events.EventHubs.Hub!),
            ValidationReports = fleet.Projections,
        };

        app.MapGroup("/datasets/{dataset}").RequireRateLimiting("reads").MapVarveDataset(options);
        app.MapVarveManagement(options, fleet);
        MapManagementSparql(app, options, fleet, updates);
        app.MapVarveEventTypes("/events/types", options).RequireRateLimiting("reads");
        app.MapGet("/health/live", (RequestDelegate)(context => WriteJsonAsync(context, StatusCodes.Status200OK, "application/json", json => json.WriteString("status", "live")))).RequireRateLimiting("health");
        app.MapGet("/health/ready", (RequestDelegate)(context => ReadyAsync(context, fleet, settings, drain))).RequireRateLimiting("health");
    }

    // Ready when every dataset that should be open is open, no default
    // projection is failed or behind the allowed lag, the host is not
    // draining (ADRs 0101, 0106, 0113), and the management plane is applied
    // within its lag, not superseded, with the bootstrap operators recorded
    // as configured (ADR 0163 point 13). A closed dataset is reported and
    // does not count.
    private static Task ReadyAsync(HttpContext context, Fleet fleet, ServerSettings settings, Drain drain)
    {
        OpenDatasets datasets = fleet.Datasets;
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
                open && dataset!.IsFailed,
                open && datasets.IsSuperseded(entry.Name),
                entry.Role,
                open ? dataset!.Retention : null));
        }

        ManagementStatus management = fleet.Status;
        (bool ready, List<DatasetReadiness> states) = Readiness.Evaluate(health, settings.Health.ReadyLag, drain.Draining, management, settings.Fleet.AllowedLag);
        context.Response.Headers.CacheControl = "no-store";

        void Management(Utf8JsonWriter json)
        {
            json.WriteStartObject("management");
            json.WriteString("role", management.Role switch { ManagementRole.Writer => "writer", ManagementRole.Follower => "follower", _ => "superseded" });
            json.WriteNumber("epoch", management.Epoch);
            json.WriteNumber("position", management.Applied.Value);
            json.WriteNumber("lag", management.Lag);
            json.WriteString("bootstrap", management.Bootstrap switch { BootstrapState.Recorded => "recorded", BootstrapState.Absent => "absent", _ => "mismatch" });
            json.WriteEndObject();
        }

        static string Role(DatasetRole role) => role switch { DatasetRole.Writer => "writer", DatasetRole.Follower => "follower", _ => "elsewhere" };

        // Each binding's validation projection lag (ADR 0174): reported, and never a reason to be unready.
        void Validation(Utf8JsonWriter json, string name)
        {
            if (!DatasetName.TryParse(name, out DatasetName dataset))
            {
                return;
            }

            SortedDictionary<string, long> lags = fleet.Projections.LagOf(dataset);
            if (lags.Count == 0)
            {
                return;
            }

            json.WriteStartObject("validation");
            foreach ((string binding, long lag) in lags)
            {
                json.WriteNumber(binding, lag);
            }

            json.WriteEndObject();
        }

        if (ready)
        {
            return WriteJsonAsync(context, StatusCodes.Status200OK, "application/json", json =>
            {
                json.WriteString("status", "ready");
                Management(json);
                json.WriteStartObject("datasets");

                foreach (DatasetReadiness state in states)
                {
                    json.WriteStartObject(state.Name);
                    json.WriteString("state", state.State);
                    json.WriteString("role", Role(state.Role));

                    if (state.Lag is long lag)
                    {
                        json.WriteNumber("lag", lag);
                    }

                    Validation(json, state.Name);
                    json.WriteEndObject();
                }

                json.WriteEndObject();
                Readiness.WriteRetention(json, health);
            });
        }

        return WriteProblemAsync(context, ProblemType.NotReady, drain.Draining ? "The server is draining." : null, json =>
        {
            Management(json);
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
                json.WriteString("role", Role(state.Role));

                if (state.Reason is not null)
                {
                    json.WriteString("reason", state.Reason);
                }

                if (state.Lag is long lag)
                {
                    json.WriteNumber("lag", lag);
                }

                Validation(json, state.Name);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            Readiness.WriteRetention(json, health);
        });
    }

    // The dataset a data-plane path names: /datasets/{name}/….
    private static DatasetName? DatasetOf(PathString path)
    {
        if (!path.StartsWithSegments("/datasets", out PathString rest) || rest.Value is not { Length: > 1 } tail)
        {
            return null;
        }

        int end = tail.IndexOf('/', 1);
        return DatasetName.TryParse(end < 0 ? tail[1..] : tail[1..end], out DatasetName name) ? name : null;
    }

    /// <summary>The header a request names the least management position it may be served at with (ADR 0163 point 3).</summary>
    internal const string MinManagementPositionHeader = "Varve-Min-Management-Position";

    /// <summary>The header every response names the management position it was served at with.</summary>
    internal const string ManagementPositionHeader = "Varve-Management-Position";

    // Pins the grants for the request. Asked for a later position than is
    // applied, the system follower is advanced once; still behind, the
    // request is 503 management-behind, with no wait (ADR 0163 point 3).
    private static Func<HttpContext, RequestDelegate, Task> ManagementPositionAsync(Fleet fleet) => async (context, next) =>
    {
        Microsoft.Extensions.Primitives.StringValues asked = context.Request.Headers[MinManagementPositionHeader];

        if (!Microsoft.Extensions.Primitives.StringValues.IsNullOrEmpty(asked))
        {
            if (asked.Count != 1 || !long.TryParse(asked[0], NumberStyles.None, CultureInfo.InvariantCulture, out long wanted))
            {
                await WriteProblemAsync(context, ProblemType.BadRequest, MinManagementPositionHeader + " is one position.").ConfigureAwait(false);
                return;
            }

            if (wanted > fleet.Current.Table.Position.Value)
            {
                await fleet.AdvanceAsync(context.RequestAborted).ConfigureAwait(false);
            }

            long applied = fleet.Current.Table.Position.Value;

            if (wanted > applied)
            {
                context.Response.Headers[ManagementPositionHeader] = applied.ToString(CultureInfo.InvariantCulture);
                await WriteProblemAsync(context, ProblemType.ManagementBehind, "This instance has applied management position " + applied.ToString(CultureInfo.InvariantCulture) + ".", json =>
                {
                    json.WriteNumber("position", wanted);
                    json.WriteNumber("appliedPosition", applied);
                }).ConfigureAwait(false);
                return;
            }
        }

        // A write to a dataset whose log already records a later management
        // position than this instance applied: the system follower is
        // advanced once before the write is authorised, so it is decided by
        // grants at least that recent (ADR 0165 point 5).
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
            && DatasetOf(context.Request.Path) is { } written
            && fleet.Recorded(written).Value > fleet.Current.Table.Position.Value)
        {
            await fleet.AdvanceAsync(context.RequestAborted).ConfigureAwait(false);
        }

        Fleet.Snapshot snapshot = fleet.Current;
        Fleet.Pin(context, snapshot);
        context.Response.OnStarting(() =>
        {
            // A forwarded answer carries the writer's position, which it keeps.
            if (!context.Response.Headers.ContainsKey(ManagementPositionHeader))
            {
                context.Response.Headers[ManagementPositionHeader] = snapshot.Table.Position.Value.ToString(CultureInfo.InvariantCulture);
            }

            return Task.CompletedTask;
        });
        await next(context).ConfigureAwait(false);
    };

    // /manage/sparql (ADR 0163 point 1): SPARQL over the system dataset, for
    // an operator. An update runs the code validator, and goes to the system
    // writer like every management write.
    private static void MapManagementSparql(WebApplication app, ProtocolOptions options, Fleet fleet, StoreUpdates updates)
    {
        ProtocolOptions system = new()
        {
            Datasets = new SystemResolver(fleet),
            Updates = new SystemUpdates(updates, fleet),
            Writers = options.Writers is null ? null : new SystemDirectory(fleet),
            Forwarder = options.Forwarder,
            Identity = options.Identity,
            AccessScopes = EveryoneEverything.Instance,
            Authorization = new OperatorsOnly(options.Authorization),
            Clock = options.Clock,
            Limits = options.Limits,
            Evaluation = options.Evaluation,
            Stopping = options.Stopping,
            Telemetry = options.Telemetry,
            Logger = options.Logger,
        };
        app.MapSparqlEndpoint("/manage/sparql", system);
    }

    /// <summary>The system dataset as the one dataset a resolver knows.</summary>
    private sealed class SystemResolver(Fleet fleet) : IDatasetResolver
    {
        public bool TryResolve(DatasetName name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dataset? dataset)
        {
            dataset = fleet.System;
            return true;
        }
    }

    /// <summary>Every write to the system dataset goes to its writer.</summary>
    private sealed class SystemDirectory(Fleet fleet) : IWriterDirectory
    {
        public WriterName Instance => fleet.Instance;

        public ValueTask<WriterRoute> RouteAsync(WriterTarget target, CancellationToken cancellationToken) => fleet.RouteAsync(WriterTarget.System, cancellationToken);

        public void Stale(WriterTarget target) => fleet.Stale(WriterTarget.System);
    }

    /// <summary>An update of the system dataset, checked by the code validator (ADR 0166).</summary>
    private sealed class SystemUpdates(StoreUpdates inner, Fleet fleet) : ISparqlUpdateExecutor
    {
        public ValueTask<CommitResult> ExecuteAsync(Dataset dataset, Update update, CommitMetadata metadata, UpdatePreconditions preconditions, Varve.Rdf.CallerScope scope, CancellationToken cancellationToken) =>
            inner.ExecuteAsync(dataset, update, metadata, new UpdatePreconditions
            {
                ExpectedPosition = preconditions.ExpectedPosition,
                ExpectedGraphs = preconditions.ExpectedGraphs,
                Validators = [.. preconditions.Validators, fleet.Validator],
            }, scope, cancellationToken);
    }

    /// <summary>Every permission on the system dataset is the operator's.</summary>
    private sealed class OperatorsOnly(IAuthorizationService inner) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            inner.AuthorizeAsync(user, null, DatasetPermissions.ServerAdmin);

        public Task<AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource, string policyName) =>
            inner.AuthorizeAsync(user, null, DatasetPermissions.ServerAdmin);
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

    /// <summary>The update executor (ADR 0091): Varve.Sparql.Store, the client's preconditions passed through and never retried, an inferred one retried three times, SERVICE and LOAD through the outbound client (ADR 0104), the memory budget of ADR 0114.</summary>
    private sealed class StoreUpdates(TimeProvider clock, Outbound outbound, MemoryBytes budget) : ISparqlUpdateExecutor
    {
        public ValueTask<CommitResult> ExecuteAsync(Dataset dataset, Update update, CommitMetadata metadata, UpdatePreconditions preconditions, Varve.Rdf.CallerScope scope, CancellationToken cancellationToken) =>
            SparqlUpdate.ExecuteAsync(dataset, update, new UpdateOptions
            {
                Metadata = metadata,
                ExpectedPosition = preconditions.ExpectedPosition,
                ExpectedGraphs = Expected(preconditions),
                Validators = preconditions.Validators,

                // A conflict with no precondition from the client is the
                // executor's own inferred scope's: re-pinned and evaluated
                // again, up to three times (ADRs 0057, 0094, amended 2026-10-13).
                ConflictRetries = preconditions.IsImplicit ? 3 : 0,
                Evaluation = new EvaluationOptions { Clock = clock, Randomness = new SystemRandomness(), ServiceHandler = outbound.Service, MemoryBudget = budget },
                LoadSource = outbound.Load,
                ReadScope = scope.Readable,
                WriteScope = scope.Writable,
            }, cancellationToken);
    }

    // Varve-If-Graphs-Match, as Varve.Sparql.Store takes it (ADR 0126).
    private static ExpectedGraphVersion[]? Expected(UpdatePreconditions preconditions)
    {
        if (preconditions.ExpectedGraphs is not { } graphs)
        {
            return null;
        }

        ExpectedGraphVersion[] expected = new ExpectedGraphVersion[graphs.Count];

        for (int i = 0; i < expected.Length; i++)
        {
            expected[i] = graphs[i].Graph is { } graph ? new ExpectedGraphVersion(graph, graphs[i].Version) : new ExpectedGraphVersion(graphs[i].Version);
        }

        return expected;
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
