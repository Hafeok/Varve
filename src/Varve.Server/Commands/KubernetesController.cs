// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Client;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve manage watch-kubernetes</c> (ADR 0181 point 8): the Kubernetes
/// handover controller, in Varve's own image. It lists the fleet's pods by
/// their label selector and watches them, through the API server, with the
/// pod's service-account token. When a pod's <c>Ready</c> condition turns
/// from <c>True</c> to anything else, or the pod is deleted while ready, it
/// takes over from that pod, as <c>varve manage takeover --from</c> does,
/// over the other pods' addresses. Kubelet's probe judges liveness and its
/// threshold is the only delay: the watch is pushed by the API server, and
/// nothing here waits on a clock but the pause before a failed watch is
/// opened again.
/// </summary>
[Contract(typeof(HandoverAutomation.LivenessIsTheOrchestratorsJudgement), Role = "the Kubernetes handover controller, acting on kubelet's verdict")]
internal static class KubernetesController
{
    private const string ServiceAccount = "/var/run/secrets/kubernetes.io/serviceaccount/";

    // The pause before a watch that failed is opened again: the API server
    // was unreachable or refused, and opening it at once would spin.
    private static readonly TimeSpan Reopen = TimeSpan.FromSeconds(2);

    internal static Command Command(Io io)
    {
        Option<string> selector = new("--selector") { Description = "The label selector of the fleet's pods, for example app.kubernetes.io/name=varve.", Required = true };
        Option<string> address = new("--address") { Description = "A pod's address, with {pod} for its name, for example http://{pod}.varve-headless.varve.svc.cluster.local:8080.", Required = true };
        Option<string?> ns = new("--namespace") { Description = "The pods' namespace; the service account's own when absent." };
        Option<string?> api = new("--kubernetes-api") { Description = "The API server; https://$KUBERNETES_SERVICE_HOST:$KUBERNETES_SERVICE_PORT when absent." };
        Option<string?> apiToken = new("--kubernetes-token") { Description = "The file holding the token for the API server, read at each request; the service account's when absent." };
        Option<string?> apiCa = new("--kubernetes-ca") { Description = "The API server's certificate authority; the service account's when absent." };
        Command watch = new("watch-kubernetes", "Watch a fleet's pods and take over from each that turns not ready (ADR 0181).");

        foreach (Option option in (Option[])[selector, address, ns, api, apiToken, apiCa])
        {
            watch.Options.Add(option);
        }

        Manage.OperatorToken credentials = new(watch);

        watch.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            string server = parsed.GetValue(api) ?? (Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") is { Length: > 0 } host
                ? "https://" + host + ":" + (Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT") ?? "443")
                : throw new Cli.CommandException("give --kubernetes-api, or run in a pod, where KUBERNETES_SERVICE_HOST is set."));
            string tokenFile = parsed.GetValue(apiToken) ?? ServiceAccount + "token";
            string? ca = parsed.GetValue(apiCa) ?? (File.Exists(ServiceAccount + "ca.crt") ? ServiceAccount + "ca.crt" : null);
            string space = parsed.GetValue(ns) ?? (File.Exists(ServiceAccount + "namespace")
                ? (await File.ReadAllTextAsync(ServiceAccount + "namespace", cancellationToken).ConfigureAwait(false)).Trim()
                : throw new Cli.CommandException("give --namespace, or run in a pod, where the service account names its namespace."));
            using HttpClient kubernetes = ApiClient(ca);
            using HttpClient varve = OutboundHttp.CreateClient();
            Watcher watcher = new(kubernetes, new Uri(server.TrimEnd('/') + "/api/v1/namespaces/" + Uri.EscapeDataString(space) + "/pods?labelSelector=" + Uri.EscapeDataString(parsed.GetValue(selector)!)), tokenFile, io);

            await watcher.RunAsync(
                async (failed, others, ct) =>
                {
                    string? bearer = await credentials.TokenAsync(parsed, varve, ct).ConfigureAwait(false);
                    string[] addresses = [.. others.Select(pod => parsed.GetValue(address)!.Replace("{pod}", pod, StringComparison.Ordinal).TrimEnd('/'))];
                    return await Takeovers.FromAsync(varve, bearer, failed, addresses, [], io, ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            return 0;
        }, parsed.InvocationConfiguration.Error));

        return watch;
    }

    // An HTTP client for the API server, trusting its own authority: the
    // service account's CA bundle, and nothing else, when one is given.
    private static HttpClient ApiClient(string? ca)
    {
        SocketsHttpHandler handler = new() { AllowAutoRedirect = false, UseCookies = false };

        if (ca is not null)
        {
            X509Certificate2Collection roots = [];
            roots.ImportFromPemFile(ca);
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
            {
                if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
                {
                    return false;
                }

                using X509Chain chain = new();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(roots);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                using X509Certificate2 presented = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                return chain.Build(presented);
            };
        }

        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// The list and the watch, and each pod's last <c>Ready</c> status.
    /// A pod seen first, or one that only turns ready, starts nothing.
    /// </summary>
    [Contract(typeof(HandoverAutomation.KubernetesSampleWatchesReadiness), Role = "the list and watch of the fleet's pods")]
    internal sealed class Watcher(HttpClient http, Uri pods, string tokenFile, Io io)
    {
        private readonly Dictionary<string, bool> _ready = new(StringComparer.Ordinal);
        private string? _version;

        /// <summary>Lists and watches until cancelled; each pod that stops being ready is handed to <paramref name="takeOver"/> with the others' names.</summary>
        internal async Task RunAsync(Func<string, string[], CancellationToken, Task<bool>> takeOver, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_version is null)
                    {
                        await ListAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await WatchAsync(takeOver, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception failure) when (failure is HttpRequestException or IOException or JsonException or Cli.CommandException)
                {
                    await io.Error.WriteLineAsync("varve: the watch of the fleet's pods failed, and is opened again: " + failure.Message).ConfigureAwait(false);

                    try
                    {
                        await Task.Delay(Reopen, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        // The pods as they are, which starts nothing, and the version to watch from.
        private async Task ListAsync(CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await SendAsync(pods, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            using JsonDocument list = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            _ready.Clear();

            if (list.RootElement.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement pod in items.EnumerateArray())
                {
                    if (NameOf(pod) is { } name)
                    {
                        _ready[name] = IsReady(pod);
                    }
                }
            }

            _version = list.RootElement.TryGetProperty("metadata", out JsonElement metadata) ? String(metadata, "resourceVersion") : null;
        }

        // One watch, from the version last seen, until the API server ends it.
        private async Task WatchAsync(Func<string, string[], CancellationToken, Task<bool>> takeOver, CancellationToken cancellationToken)
        {
            Uri url = new(pods + "&watch=true&allowWatchBookmarks=true" + (_version is null ? string.Empty : "&resourceVersion=" + Uri.EscapeDataString(_version)));
            using HttpResponseMessage response = await SendAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            using StreamReader events = new(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));

            while (await events.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                using JsonDocument change = JsonDocument.Parse(line);
                string? type = String(change.RootElement, "type");

                if (type == "ERROR" || !change.RootElement.TryGetProperty("object", out JsonElement pod))
                {
                    // 410 Gone: the version is too old to watch from; list again.
                    _version = null;
                    return;
                }

                if (pod.TryGetProperty("metadata", out JsonElement metadata) && String(metadata, "resourceVersion") is { } version)
                {
                    _version = version;
                }

                if (type == "BOOKMARK" || NameOf(pod) is not { } name)
                {
                    continue;
                }

                bool was = _ready.TryGetValue(name, out bool ready) && ready;
                bool now = type != "DELETED" && IsReady(pod);

                if (type == "DELETED")
                {
                    _ready.Remove(name);
                }
                else
                {
                    _ready[name] = now;
                }

                if (was && !now)
                {
                    await io.Out.WriteLineAsync(name + " is not ready: taking over from it.").ConfigureAwait(false);
                    string[] others = [.. _ready.Keys.Where(other => other != name).Order(StringComparer.Ordinal)];

                    if (!await takeOver(name, others, cancellationToken).ConfigureAwait(false))
                    {
                        await io.Error.WriteLineAsync("varve: the takeover from " + name + " was refused by every other pod; it stays with the next probe failure or a person.").ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task<HttpResponseMessage> SendAsync(Uri url, HttpCompletionOption completion, CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);

            // Read at each request: kubelet rotates the projected token.
            if (File.Exists(tokenFile))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", (await File.ReadAllTextAsync(tokenFile, cancellationToken).ConfigureAwait(false)).Trim());
            }

            HttpResponseMessage response = await http.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                response.Dispose();
                throw new Cli.CommandException("the API server answered " + (int)response.StatusCode + " to " + url.AbsolutePath + ": " + body);
            }

            return response;
        }

        // A pod's Ready condition, True or not.
        private static bool IsReady(JsonElement pod) =>
            pod.TryGetProperty("status", out JsonElement status)
            && status.TryGetProperty("conditions", out JsonElement conditions)
            && conditions.ValueKind == JsonValueKind.Array
            && conditions.EnumerateArray().Any(condition => String(condition, "type") == "Ready" && String(condition, "status") == "True");

        private static string? NameOf(JsonElement pod) =>
            pod.TryGetProperty("metadata", out JsonElement metadata) ? String(metadata, "name") : null;

        private static string? String(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
