// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using Varve.Protocol;

namespace Varve.Server;

/// <summary>
/// The exporter (ADR 0112): the OpenTelemetry SDK, built only when the
/// standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (or a signal's own) is set,
/// subscribed to the libraries' sources and ASP.NET Core's and the runtime's,
/// with <c>ILogger</c> routed through its log exporter. Everything else about
/// it (headers, protocol, service name, resource attributes, intervals) is
/// the <c>OTEL_*</c> environment's, read by the SDK.
/// </summary>
internal static class Telemetry
{
    /// <summary>Whether an OTLP endpoint is configured, so the SDK is built.</summary>
    internal static bool Enabled
    {
        get
        {
            ReadOnlySpan<string> variables = ["OTEL_EXPORTER_OTLP_ENDPOINT", "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"];

            foreach (string variable in variables)
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal static void Configure(WebApplicationBuilder builder)
    {
        if (!Enabled)
        {
            return;
        }

        // service.name is OTEL_SERVICE_NAME's when set, which the SDK's own
        // detector reads; "varve" is the default it would otherwise lack.
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")))
                {
                    resource.AddService("varve");
                }
            })
            .WithTracing(tracing => tracing
                .AddSource(TelemetryOptions.ActivitySourceName)
                .AddSource("Microsoft.AspNetCore"))
            .WithMetrics(metrics => metrics
                .AddMeter(TelemetryOptions.MeterName)
                .AddMeter("Microsoft.AspNetCore.Hosting")
                .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                .AddMeter("Microsoft.AspNetCore.RateLimiting")
                .AddMeter("System.Net.Http")
                .AddMeter("System.Runtime"))
            .UseOtlpExporter();
    }
}
