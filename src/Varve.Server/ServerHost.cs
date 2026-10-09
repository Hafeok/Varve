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
    // (ADRs 0101, 0106): datasets are opened, their default projections
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

    /// <summary>
    /// A problem the host writes outside the protocol's endpoints (ADR 0119),
    /// from the catalogue: the request id as instance, except on the
    /// deliberately thin <c>401</c>.
    /// </summary>
    private static async Task WriteProblemAsync(HttpContext context, ProblemType type, string? detail)
    {
        ArrayBufferWriter<byte> body = new(256);

        using (Utf8JsonWriter json = new(body))
        {
            ProblemCatalogue.Write(json, type, type == ProblemType.Unauthorized ? null : context.TraceIdentifier, detail);
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

    /// <summary>The update executor (ADR 0091): Varve.Sparql.Store, no retries, the expected position passed through, SERVICE and LOAD through the outbound client (ADR 0104).</summary>
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
