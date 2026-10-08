// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
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
    internal static async Task<int> RunAsync(string[] args, TaskCompletionSource<WebApplication>? ready)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
        ServerSettings settings = new();

        try
        {
            builder.Configuration.GetSection("Varve").Bind(settings);
        }
        catch (InvalidOperationException error)
        {
            await Console.Error.WriteLineAsync("varve: the configuration does not bind: " + error.Message).ConfigureAwait(false);
            return 2;
        }

        List<string> errors = SettingsCheck.Errors(settings);

        if (errors.Count > 0)
        {
            await Console.Error.WriteLineAsync("varve: the configuration is not valid, and the server does not start:").ConfigureAwait(false);

            foreach (string error in errors)
            {
                await Console.Error.WriteLineAsync("  - " + error).ConfigureAwait(false);
            }

            return 2;
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
        await using OpenDatasets datasets = await OpenDatasets.OpenAsync(settings, clock, CancellationToken.None).ConfigureAwait(false);
        IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        using HttpClient outbound = OutboundHttp.CreateClient();
        Map(app, settings, datasets, clock, anonymous, Outbound.Of(settings, outbound), lifetime.ApplicationStopping);

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

    private static void Configure(WebApplicationBuilder builder, ServerSettings settings, bool anonymous)
    {
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = settings.Limits.MaxRequestBody);
        IServiceCollection services = builder.Services;

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
            bearer.IncludeErrorDetails = false;
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

    private static void Map(WebApplication app, ServerSettings settings, OpenDatasets datasets, TimeProvider clock, bool anonymous, Outbound outbound, CancellationToken stopping)
    {
        if (settings.ForwardedHeaders.Enabled)
        {
            app.UseForwardedHeaders();
        }

        // On SIGTERM: a new write is refused, while what is in flight drains
        // (ADR 0101). Reads still answer until the host stops listening.
        app.Use(async (context, next) =>
        {
            if (stopping.IsCancellationRequested && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                await WriteJsonAsync(context, StatusCodes.Status503ServiceUnavailable, "application/problem+json", json =>
                {
                    json.WriteString("type", "https://w3id.org/varve/problems/shutting-down");
                    json.WriteString("title", "The server is shutting down.");
                    json.WriteNumber("status", StatusCodes.Status503ServiceUnavailable);
                }).ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        if (!anonymous)
        {
            app.UseAuthentication();
        }

        ProtocolOptions options = new()
        {
            Datasets = datasets,
            Administration = datasets,
            Updates = new StoreUpdates(clock, outbound),
            Identity = anonymous ? new NoAgent() : new TokenIdentity(settings.Auth.SubjectClaim),
            AccessScopes = anonymous ? EveryoneEverything.Instance : new GrantedScopes(settings.Auth),
            Authorization = app.Services.GetRequiredService<IAuthorizationService>(),
            Clock = clock,
            Limits = new ProtocolLimits(
                settings.Limits.QueryTimeout,
                settings.Limits.PinnedReadLifetime,
                new ByteCount(settings.Limits.ResultSizeCap),
                new ByteCount(settings.Limits.MaxRequestBody),
                settings.Limits.FeedHeartbeat),
            Evaluation = new EvaluationOptions { Clock = clock, Randomness = new SystemRandomness(), ServiceHandler = outbound.Service },
            Stopping = stopping,
        };

        app.MapGroup("/datasets/{dataset}").MapVarveDataset(options);
        app.MapVarveAdministration("/datasets", options);
        app.MapGet("/live", (RequestDelegate)(context => WriteJsonAsync(context, StatusCodes.Status200OK, "application/json", json => json.WriteString("status", "live"))));
        app.MapGet("/ready", (RequestDelegate)(context => ReadyAsync(context, datasets)));
    }

    // Ready when every dataset that should be open is open and not failed
    // (ADRs 0101, 0105): datasets are opened, their default projections
    // replayed to the head, before the server listens, so a failure to open
    // — a directory under the root that is leased elsewhere, or does not
    // parse — and a projection failure are what remain to report. A dataset
    // closed by the admin API is reported and does not count against readiness.
    private static Task ReadyAsync(HttpContext context, OpenDatasets datasets)
    {
        bool ready = true;
        List<(string Name, string State, string? Reason)> states = [];

        foreach (Varve.Protocol.Model.DatasetEntry entry in datasets.List())
        {
            string state = entry.State switch
            {
                Varve.Protocol.Model.DatasetState.Open => datasets.TryResolve(entry.Name, out Dataset? dataset) && dataset.IsFailed ? "failed" : "ready",
                Varve.Protocol.Model.DatasetState.Closed => "closed",
                _ => "failed",
            };
            ready &= state != "failed";
            states.Add((entry.Name.Value, state, entry.Reason));
        }

        return WriteJsonAsync(context, ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable, "application/json", json =>
        {
            json.WriteString("status", ready ? "ready" : "failed");
            json.WriteStartObject("datasets");

            foreach ((string name, string state, string? reason) in states)
            {
                if (reason is null)
                {
                    json.WriteString(name, state);
                }
                else
                {
                    json.WriteStartObject(name);
                    json.WriteString("state", state);
                    json.WriteString("reason", reason);
                    json.WriteEndObject();
                }
            }

            json.WriteEndObject();
        });
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

    /// <summary>The update executor (ADR 0091): Varve.Sparql.Store, no retries, the expected position passed through, SERVICE and LOAD through the outbound client (ADR 0103).</summary>
    private sealed class StoreUpdates(TimeProvider clock, Outbound outbound) : ISparqlUpdateExecutor
    {
        public ValueTask<CommitResult> ExecuteAsync(Dataset dataset, Update update, CommitMetadata metadata, Position? expectedPosition, Varve.Rdf.CallerScope scope, CancellationToken cancellationToken) =>
            SparqlUpdate.ExecuteAsync(dataset, update, new UpdateOptions
            {
                Metadata = metadata,
                ExpectedPosition = expectedPosition,
                Evaluation = new EvaluationOptions { Clock = clock, Randomness = new SystemRandomness(), ServiceHandler = outbound.Service },
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
