// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Serves a recorded scenario over HTTP, for testing the Action end to end.
//
//   dotnet run replay-server.cs -- <fixture.json> --port <n> [--declaration <path>]
//
// The Action downloads the native binary and runs it against `api-url`; this
// is what stands at that URL in the test. It answers each request with the
// recorded response for the same method and path — for GraphQL, the same
// operation — in recorded order where a request repeats. A request with no
// recording is answered 599 and logged, which the binary reports as a failed
// read and so fails the step.
//
// --declaration writes the scenario's declaration to a file first, so the
// workflow can hand the Action the file the recording was made against.
//
// It prints "listening" once it accepts requests. Exit codes: 0 stopped,
// 2 could not run.

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

string? fixture = null;
int port = 0;
string? declaration = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" when i + 1 < args.Length:
            port = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--declaration" when i + 1 < args.Length:
            declaration = args[++i];
            break;
        default:
            fixture = args[i];
            break;
    }
}

if (fixture is null || port == 0)
{
    Console.Error.WriteLine("usage: replay-server.cs <fixture.json> --port <n> [--declaration <path>]");
    return 2;
}

JsonObject scenario = (JsonObject)JsonNode.Parse(File.ReadAllText(fixture))!;

if (declaration is not null)
{
    File.WriteAllText(declaration, (string?)scenario["declaration"] ?? string.Empty);
}

Dictionary<string, Queue<JsonObject>> responses = new(StringComparer.Ordinal);
foreach (JsonObject exchange in ((JsonArray)scenario["exchanges"]!).OfType<JsonObject>())
{
    JsonObject request = (JsonObject)exchange["request"]!;
    string key = Key((string)request["method"]!, (string)request["path"]!, (string?)request["operation"]);
    if (!responses.TryGetValue(key, out Queue<JsonObject>? queue))
    {
        queue = new Queue<JsonObject>();
        responses[key] = queue;
    }

    queue.Enqueue((JsonObject)exchange["response"]!);
}

using HttpListener listener = new();
listener.Prefixes.Add($"http://127.0.0.1:{port}/");
listener.Start();
Console.WriteLine($"listening on http://127.0.0.1:{port}/ with {responses.Sum(r => r.Value.Count)} recorded responses");

Regex operationName = new(@"^\s*(?:query|mutation)\s+(\w+)", RegexOptions.CultureInvariant);

while (listener.IsListening)
{
    HttpListenerContext context = await listener.GetContextAsync();
    string path = context.Request.Url!.PathAndQuery;
    string? operation = null;

    if (path.EndsWith("/graphql", StringComparison.Ordinal))
    {
        using StreamReader reader = new(context.Request.InputStream, Encoding.UTF8);
        JsonNode? body = JsonNode.Parse(await reader.ReadToEndAsync());
        operation = operationName.Match((string?)body?["query"] ?? string.Empty).Groups[1].Value;
        path = "/graphql";
    }

    string key = Key(context.Request.HttpMethod, path, operation);
    if (!responses.TryGetValue(key, out Queue<JsonObject>? queue) || queue.Count == 0)
    {
        Console.Error.WriteLine($"no recording for {key}");
        context.Response.StatusCode = 599;
        context.Response.Close();
        continue;
    }

    JsonObject response = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
    Console.WriteLine($"{key} -> {(int?)response["status"] ?? 200}");
    context.Response.StatusCode = (int?)response["status"] ?? 200;
    foreach ((string name, JsonNode? value) in response["headers"] as JsonObject ?? [])
    {
        // A Link header names GitHub's host; the next page is here.
        context.Response.Headers[name] = ((string?)value)?.Replace("https://api.github.com", $"http://127.0.0.1:{port}", StringComparison.Ordinal);
    }

    byte[] bytes = response["body"] is JsonNode content ? Encoding.UTF8.GetBytes(content.ToJsonString()) : [];
    context.Response.ContentType = "application/json";
    context.Response.ContentLength64 = bytes.Length;
    await context.Response.OutputStream.WriteAsync(bytes);
    context.Response.Close();
}

return 0;

static string Key(string method, string path, string? operation) =>
    operation is null ? $"{method} {path}" : $"GraphQL {operation}";
