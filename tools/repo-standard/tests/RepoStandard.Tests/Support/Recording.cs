// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.GitHub;
using RepoStandard.Json;

namespace RepoStandard.Tests.Support;

/// <summary>One request and the response GitHub gave, or documents, for it.</summary>
/// <remarks>
/// A REST request is its method, path with query, and JSON body. A GraphQL
/// request is its operation name and variables: the query text is the code's,
/// and pinning it here would test the text rather than the contract.
/// </remarks>
internal sealed record Exchange(
    string Method,
    string Path,
    JsonNode? Body,
    string? Operation,
    int Status,
    Dictionary<string, string> Headers,
    JsonNode? ResponseBody)
{
    public static Exchange From(JsonObject node)
    {
        JsonObject request = (JsonObject)node["request"]!;
        JsonObject response = (JsonObject)node["response"]!;
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, JsonNode? value) in response["headers"] as JsonObject ?? [])
        {
            headers[name] = value!.GetValue<string>();
        }

        return new Exchange(
            JsonTree.Str(request, "method")!,
            JsonTree.Str(request, "path")!,
            request["body"]?.DeepClone(),
            JsonTree.Str(request, "operation"),
            (int)(JsonTree.Int(response, "status") ?? 200),
            headers,
            response["body"]?.DeepClone());
    }

    public string Describe() => Operation is null ? $"{Method} {Path}" : $"GraphQL {Operation}";
}

/// <summary>A recorded scenario: a declaration, the command run on it, the exchanges, and what it printed.</summary>
internal sealed record Recording(
    string Name,
    string Description,
    string Source,
    string[] Command,
    string Declaration,
    List<Exchange> Exchanges,
    int ExpectExit,
    string ExpectOutput)
{
    public const string Repository = "octo-org/hello-world";

    public static string Directory => Path.Combine(AppContext.BaseDirectory, "fixtures", "recorded");

    public static IEnumerable<Recording> All() =>
        System.IO.Directory.EnumerateFiles(Directory, "*.json").Order(StringComparer.Ordinal).Select(Load);

    public static Recording Load(string path)
    {
        JsonObject node = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        return new Recording(
            Path.GetFileNameWithoutExtension(path),
            JsonTree.Str(node, "description") ?? string.Empty,
            JsonTree.Str(node, "source") ?? string.Empty,
            [.. JsonTree.Items(node, "command").Select(c => c!.GetValue<string>())],
            JsonTree.Str(node, "declaration") ?? string.Empty,
            [.. JsonTree.Items(node, "exchanges").OfType<JsonObject>().Select(Exchange.From)],
            (int)(JsonTree.Int(node["expect"], "exit") ?? 0),
            File.Exists(Path.ChangeExtension(path, ".out")) ? File.ReadAllText(Path.ChangeExtension(path, ".out")) : string.Empty);
    }
}

/// <summary>
/// Answers requests from a recording, in order, and fails the test on the first
/// request that is not the next one recorded.
/// </summary>
internal sealed partial class ReplayHandler(IEnumerable<Exchange> exchanges) : HttpMessageHandler
{
    private readonly Queue<Exchange> _pending = new(exchanges);

    public List<string> Mismatches { get; } = [];

    public List<Exchange> Served { get; } = [];

    public IReadOnlyCollection<Exchange> Remaining => _pending;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.PathAndQuery;
        string? text = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? body = string.IsNullOrEmpty(text) ? null : JsonNode.Parse(text);

        string? operation = null;
        JsonNode? variables = null;
        if (path == "/graphql")
        {
            operation = OperationName().Match(JsonTree.Str(body, "query") ?? string.Empty).Groups[1].Value;
            variables = body?["variables"];
        }

        string actual = operation is null ? $"{request.Method} {path} {Canonical(body)}" : $"GraphQL {operation} {Canonical(variables)}";

        if (!_pending.TryDequeue(out Exchange? next))
        {
            Mismatches.Add($"unexpected request, none left: {actual}");
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }

        string expected = next.Operation is null
            ? $"{next.Method} {next.Path} {Canonical(next.Body)}"
            : $"GraphQL {next.Operation} {Canonical(next.Body)}";

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            Mismatches.Add($"expected {expected}\n  actual {actual}");
        }

        Served.Add(next);
        return Respond(next);
    }

    public static HttpResponseMessage Respond(Exchange exchange)
    {
        HttpResponseMessage response = new((HttpStatusCode)exchange.Status);
        if (exchange.ResponseBody is not null)
        {
            response.Content = new StringContent(exchange.ResponseBody.ToJsonString(), Encoding.UTF8, "application/json");
        }
        else
        {
            response.Content = new StringContent(string.Empty);
        }

        foreach ((string name, string value) in exchange.Headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    private static string Canonical(JsonNode? node) => node is null ? "-" : JsonTree.Canonical(node);

    [GeneratedRegex(@"^\s*(?:query|mutation)\s+(\w+)")]
    private static partial Regex OperationName();
}

/// <summary>Which catalogued endpoint a recorded request is.</summary>
internal static class EndpointMatch
{
    public static Endpoint? Of(Exchange exchange)
    {
        foreach (Endpoint endpoint in Endpoints.All)
        {
            if (!string.Equals(endpoint.Method, exchange.Method, StringComparison.Ordinal))
            {
                continue;
            }

            string pattern = "^" + Regex.Replace(Regex.Escape(endpoint.Template), @"\\\{[a-z_]+}", "[^/?]+") + "$";
            if (Regex.IsMatch(exchange.Path, pattern))
            {
                return endpoint;
            }
        }

        return null;
    }
}
