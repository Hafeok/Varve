// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Varve.Rdf;

namespace Varve.Conformance.Tests;

/// <summary>A status the manifest accepts: one code, or a class (<c>hts:StatusCode2xx</c>).</summary>
internal readonly record struct StatusRange(int Low, int High)
{
    internal bool Contains(int status) => status >= Low && status <= High;

    public override string ToString() => Low == High ? Low.ToString(System.Globalization.CultureInfo.InvariantCulture) : Low / 100 + "xx";
}

/// <summary>What one request of a case expects back.</summary>
internal sealed record ProtocolExpectation(
    IReadOnlyList<StatusRange> Statuses,
    bool? Boolean,
    string? Format,
    string? Body,
    string? ContentType,
    bool CapturesLocation);

/// <summary>One request of a case, as the manifest gives it.</summary>
internal sealed record ProtocolRequest(
    string Method,
    string Path,
    IReadOnlyList<(string Name, string Value)> Headers,
    byte[]? Body,
    ProtocolExpectation Expect);

/// <summary>
/// One protocol case: its requests, run in order on one connection, and the
/// graphs loaded before them. A sequential case is one step of a suite whose
/// steps share one store (<c>http-rdf-update</c>).
/// </summary>
internal sealed record ProtocolEntry(
    string TestIri,
    string Suite,
    string Name,
    IReadOnlyList<(string File, string Graph)> GraphData,
    IReadOnlyList<ProtocolRequest> Requests,
    bool Sequential);

/// <summary>
/// The protocol suites (ADR 0092): <c>sparql11/protocol</c> and
/// <c>sparql11/graph-store-protocol</c> by their <c>ht:</c> requests;
/// <c>sparql11/http-rdf-update</c>, deprecated, by its transcripts; and
/// <c>sparql11/service-description</c>, whose three entries carry no action and
/// are checks of ours (<see cref="ServiceDescriptionChecks"/>).
/// </summary>
internal static class ProtocolCatalogue
{
    internal const string Host = "www.example";
    internal const string GraphStore = "gsp";

    private const string Mf = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#";
    private const string Ht = "http://www.w3.org/2011/http#";
    private const string Hts = "http://www.w3.org/2011/http-statusCodes#";
    private const string Cnt = "http://www.w3.org/2011/content#";
    private const string Ut = "http://www.w3.org/2009/sparql/tests/test-update#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";

    /// <summary>The suites, by id, with their manifests.</summary>
    internal static IReadOnlyList<(string Id, string[] Manifests)> Suites { get; } =
    [
        ("sparql11/protocol", ["sparql/sparql11/protocol/manifest.ttl"]),
        ("sparql11/graph-store-protocol", ["sparql/sparql11/graph-store-protocol/manifest-direct.ttl", "sparql/sparql11/graph-store-protocol/manifest-indirect.ttl"]),
        ("sparql11/http-rdf-update", ["sparql/sparql11/http-rdf-update/manifest.ttl"]),
    ];

    private static readonly Lazy<IReadOnlyList<ProtocolEntry>> AllEntries = new(ReadAll);

    internal static IReadOnlyList<ProtocolEntry> Entries => AllEntries.Value;

    internal static IReadOnlyDictionary<string, ProtocolEntry> ByIri { get; } =
        new Lazy<Dictionary<string, ProtocolEntry>>(() => Entries.ToDictionary(e => e.TestIri, StringComparer.Ordinal)).Value;

    private static List<ProtocolEntry> ReadAll()
    {
        List<ProtocolEntry> all = [];

        foreach ((string id, string[] manifests) in Suites)
        {
            foreach (string path in manifests)
            {
                ManifestGraph graph = ManifestGraph.Load(TestData.ResolveFromRoot(path), EvaluationSuite.PublishedRoot + path);
                RdfTerm manifest = graph.Subjects(Rdf + "type", Mf + "Manifest").Single();

                foreach (RdfTerm list in graph.Objects(manifest, Mf + "entries"))
                {
                    foreach (RdfTerm entry in graph.Collection(list))
                    {
                        string name = graph.Object(entry, Mf + "name") is { } n ? ManifestGraph.Text(n) : ManifestGraph.Text(entry);
                        List<(string, string)> data = [.. graph.Objects(entry, Ut + "graphData").Select(g => (
                            ManifestGraph.Text(graph.Object(g, Ut + "graph")!),
                            ManifestGraph.Text(graph.Object(g, Rdfs + "label")!)))];
                        List<ProtocolRequest> requests = id == "sparql11/http-rdf-update"
                            ? Transcript(graph, entry)
                            : Requests(graph, entry);
                        all.Add(new ProtocolEntry(ManifestGraph.Text(entry), id, name, data, requests, Sequential: id == "sparql11/http-rdf-update"));
                    }
                }
            }
        }

        return all;
    }

    private static List<ProtocolRequest> Requests(ManifestGraph graph, RdfTerm entry)
    {
        List<ProtocolRequest> requests = [];
        RdfTerm action = graph.Object(entry, Mf + "action")!;

        foreach (RdfTerm list in graph.Objects(action, Ht + "requests"))
        {
            foreach (RdfTerm request in graph.Collection(list))
            {
                List<(string, string)> headers = Headers(graph, request);
                byte[]? body = null;

                if (graph.Object(request, Ht + "body") is { } content)
                {
                    string chars = ManifestGraph.Text(graph.Object(content, Cnt + "chars")!);
                    string encoding = graph.Object(content, Cnt + "characterEncoding") is { } e ? ManifestGraph.Text(e) : "UTF-8";
                    bool multipart = headers.Any(h => h.Item1.Equals("content-type", StringComparison.OrdinalIgnoreCase) && h.Item2.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase));
                    body = Encode(Dedent(chars, multipart), encoding);
                }

                RdfTerm response = graph.Object(request, Ht + "resp")!;
                List<StatusRange> statuses = [.. graph.Objects(response, Mf + "expectedStatus").Select(s => Status(ManifestGraph.Text(s)))];
                string? expectedBody = graph.Object(response, Ht + "body") is { } b ? Dedent(ManifestGraph.Text(graph.Object(b, Cnt + "chars")!), multipart: false) : null;
                string? contentType = Headers(graph, response).Where(h => h.Item1.Equals("content-type", StringComparison.OrdinalIgnoreCase)).Select(h => h.Item2).FirstOrDefault();

                requests.Add(new ProtocolRequest(
                    ManifestGraph.Text(graph.Object(request, Ht + "methodName")!),
                    ManifestGraph.Text(graph.Object(request, Ht + "absolutePath")!),
                    headers,
                    body,
                    new ProtocolExpectation(
                        statuses,
                        graph.Object(response, Mf + "expectedBoolean") is { } boolean ? ManifestGraph.Text(boolean) == "true" : null,
                        graph.Object(response, Mf + "expectedFormat") is { } format ? ManifestGraph.Text(format) : null,
                        expectedBody,
                        contentType,
                        graph.Object(response, Mf + "expectedLocation") is not null)));
            }
        }

        return requests;
    }

    private static List<(string, string)> Headers(ManifestGraph graph, RdfTerm node)
    {
        List<(string, string)> headers = [];

        foreach (RdfTerm list in graph.Objects(node, Ht + "headers"))
        {
            foreach (RdfTerm header in graph.Collection(list))
            {
                headers.Add((ManifestGraph.Text(graph.Object(header, Ht + "fieldName")!), ManifestGraph.Text(graph.Object(header, Ht + "fieldValue")!)));
            }
        }

        return headers;
    }

    private static StatusRange Status(string iri) => iri[Hts.Length..] switch
    {
        "StatusCode2xx" => new(200, 299),
        "StatusCode3xx" => new(300, 399),
        "StatusCode4xx" => new(400, 499),
        "StatusCode5xx" => new(500, 599),
        "OK" => new(200, 200),
        "Created" => new(201, 201),
        "NoContent" => new(204, 204),
        "NotFound" => new(404, 404),
        string other => throw new InvalidOperationException("A status this harness does not know: " + other),
    };

    // http-rdf-update's transcripts: the request line, its headers and body,
    // then the response's status line, headers and body, each indented four
    // spaces under "#### Request" and "#### Response".
    private static List<ProtocolRequest> Transcript(ManifestGraph graph, RdfTerm entry)
    {
        string text = graph.Objects(entry, Rdfs + "comment").Select(ManifestGraph.Text).Single(c => c.Contains("#### Request", StringComparison.Ordinal));
        int start = text.IndexOf("#### Request", StringComparison.Ordinal) + "#### Request".Length;
        int split = text.IndexOf("#### Response", StringComparison.Ordinal);
        List<string> request = Indented(text[start..split]);
        List<string> response = Indented(text[(split + "#### Response".Length)..]);

        string[] line = Substitute(request[0]).Split(' ');
        int blank = request.IndexOf(string.Empty);
        List<(string, string)> headers = [.. request.Skip(1).Take((blank < 0 ? request.Count : blank) - 1)
            .Select(h => (h[..h.IndexOf(':', StringComparison.Ordinal)].Trim(), Substitute(h[(h.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim())))];
        bool multipart = headers.Any(h => h.Item2.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase));
        string? body = blank < 0 ? null : Substitute(string.Join(multipart ? "\r\n" : "\n", request.Skip(blank + 1)));

        int code = int.Parse(response[0].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        int responseBlank = response.IndexOf(string.Empty);
        List<string> responseHeaders = [.. response.Skip(1).Take((responseBlank < 0 ? response.Count : responseBlank) - 1)];
        string? expectedBody = responseBlank < 0 ? null : Substitute(string.Join("\n", response.Skip(responseBlank + 1)));
        string? contentType = responseHeaders.Where(h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)).Select(h => h["Content-Type:".Length..].Trim()).FirstOrDefault();

        // The transcripts name one code where the Graph Store Protocol allows
        // two: 200 and 204 are both success with no body to read (§5.3, §5.4).
        List<StatusRange> statuses = code is 200 or 204 && expectedBody is null && !line[0].Equals("GET", StringComparison.Ordinal)
            ? [new(200, 200), new(204, 204)]
            : [new(code, code)];

        string path = line[1].StartsWith('/') || line[1].StartsWith('$') ? line[1] : "/" + line[1];
        return
        [
            new ProtocolRequest(line[0], path, headers, body is null ? null : Encoding.UTF8.GetBytes(body),
                new ProtocolExpectation(statuses, null, null, string.IsNullOrWhiteSpace(expectedBody) ? null : expectedBody, contentType,
                    responseHeaders.Any(h => h.StartsWith("Location:", StringComparison.OrdinalIgnoreCase)))),
        ];
    }

    private static List<string> Indented(string text) =>
        [.. text.Split('\n').SkipWhile(l => l.Trim().Length == 0).Reverse().SkipWhile(l => l.Trim().Length == 0 || l.Trim() == "\"\"\" .").Reverse()
            .Select(l => l.StartsWith("    ", StringComparison.Ordinal) ? l[4..] : l.Trim())];

    private static string Substitute(string text) => text.Replace("$HOST$", Host, StringComparison.Ordinal).Replace("$GRAPHSTORE$", GraphStore, StringComparison.Ordinal);

    // The manifests indent their bodies; Turtle does not care, but a multipart
    // body's boundaries must start their lines, and its lines end in CRLF.
    private static string Dedent(string chars, bool multipart)
    {
        if (!multipart)
        {
            return chars;
        }

        string[] lines = chars.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\r\n", lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart())).Trim() + "\r\n";
    }

    private static byte[] Encode(string chars, string encoding) =>
        encoding.Equals("UTF-16", StringComparison.OrdinalIgnoreCase) ? Encoding.Unicode.GetBytes(chars) : Encoding.UTF8.GetBytes(chars);
}
