// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Varve.Protocol;
using Varve.Rdf;
using Varve.Sparql.Results;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;

namespace Varve.Conformance.Tests;

/// <summary>
/// The store a protocol case runs over: the memory backend, the file backend
/// in a fresh directory, or a server already running (ADR 0111: the
/// container CI built), named by <c>VARVE_SERVER_URL</c>.
/// </summary>
internal enum ProtocolSubject
{
    Memory,
    File,
    Server,
}

/// <summary>Which subjects a run covers: the two in-process ones, or the server <c>VARVE_SERVER_URL</c> names and nothing else.</summary>
internal static class ProtocolSubjects
{
    internal const string ServerVariable = "VARVE_SERVER_URL";

    /// <summary>The one dataset the server cases use, created and deleted per case through the admin API.</summary>
    internal const string ServerDataset = "gsp";

    internal static Uri? ServerAddress { get; } = Environment.GetEnvironmentVariable(ServerVariable) is { Length: > 0 } url ? new Uri(url.TrimEnd('/') + "/") : null;

    internal static IReadOnlyList<string> Names { get; } = ServerAddress is null ? ["memory", "file"] : ["server"];

    internal static ProtocolSubject Parse(string name) => name switch
    {
        "file" => ProtocolSubject.File,
        "server" => ProtocolSubject.Server,
        _ => ProtocolSubject.Memory,
    };
}

/// <summary>
/// Where a case's requests go: an in-process host, or the running server
/// with the manifests' paths moved under its dataset.
/// </summary>
internal sealed class Endpoint : IAsyncDisposable
{
    private readonly ProtocolTestHost? _host;

    private Endpoint(HttpClient client, ProtocolTestHost? host)
    {
        Client = client;
        _host = host;
    }

    internal HttpClient Client { get; }

    internal static Endpoint InProcess(ProtocolTestHost host) => new(host.Client, host);

    internal static Endpoint Server(HttpClient client) => new(client, null);

    /// <summary>
    /// The manifests' path on this endpoint. In process the endpoints are
    /// mounted where the manifests address them: <c>/sparql</c>, <c>/gsp</c>,
    /// <c>/</c>. On the server they are the dataset's: <c>/datasets/gsp/sparql</c>,
    /// <c>/datasets/gsp/graphs</c>, <c>/datasets/gsp/</c>; and a graph the
    /// manifests name by the store's address, <c>http://www.example/gsp/…</c>,
    /// is named by the server's, which is what it mints for a directly
    /// identified graph. A path captured from a <c>Location</c> is the
    /// server's already.
    /// </summary>
    internal string Rewrite(string path)
    {
        if (_host is not null)
        {
            return path;
        }

        string prefix = "/datasets/" + ProtocolSubjects.ServerDataset;

        if (path.StartsWith(prefix + "/", StringComparison.Ordinal) || path == prefix)
        {
            return RewriteText(path);
        }

        string moved = path is "" or "/"
            ? prefix + "/"
            : path == "/" + ProtocolCatalogue.GraphStore || path.StartsWith("/" + ProtocolCatalogue.GraphStore + "/", StringComparison.Ordinal) || path.StartsWith("/" + ProtocolCatalogue.GraphStore + "?", StringComparison.Ordinal)
                ? prefix + "/graphs" + path[(ProtocolCatalogue.GraphStore.Length + 1)..]
                : prefix + (path.StartsWith('/') ? path : "/" + path);
        return RewriteText(moved);
    }

    /// <summary>The store's address in <paramref name="text"/>, a body or a query string, as the server's; unchanged in process.</summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
    internal string? RewriteText(string? text)
    {
        if (_host is not null || text is null)
        {
            return text;
        }

        string store = "http://" + ProtocolCatalogue.Host + "/" + ProtocolCatalogue.GraphStore;
        string graphs = "http://" + ProtocolCatalogue.Host + "/datasets/" + ProtocolSubjects.ServerDataset + "/graphs";
        return text
            .Replace(store, graphs, StringComparison.Ordinal)
            .Replace(Uri.EscapeDataString(store), Uri.EscapeDataString(graphs), StringComparison.Ordinal);
    }

    public ValueTask DisposeAsync() => _host?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>
/// Runs a protocol case against an in-process server (Kestrel on loopback,
/// ADR 0099) over one subject, with the endpoints mounted where the manifests
/// address them: the SPARQL endpoint at <c>/sparql/</c>, the Graph Store at
/// <c>/gsp</c>, the service description at <c>/</c>. Requests carry
/// <c>Host: www.example</c>, so a directly identified graph's IRI is the one
/// the manifests name.
/// </summary>
internal static class ProtocolRunner
{
    private static readonly ConcurrentDictionary<ProtocolSubject, Task<Dictionary<string, string?>>> Sequences = new();

    /// <summary>Null when the case passes, otherwise why not.</summary>
    internal static async Task<string?> RunAsync(ProtocolEntry entry, ProtocolSubject subject, CancellationToken cancellationToken)
    {
        if (entry.Sequential)
        {
            // One store for the whole deprecated suite, whose steps depend on
            // the ones before them; each step is its own ratchet line.
            Dictionary<string, string?> results = await Sequences.GetOrAdd(subject, s => RunSequenceAsync(s, CancellationToken.None)).ConfigureAwait(false);
            return results[entry.TestIri];
        }

        await using Store store = await Store.OpenAsync(subject, cancellationToken).ConfigureAwait(false);

        foreach ((string file, string graph) in entry.GraphData)
        {
            await store.LoadAsync(EvaluationData.Quads(file), RdfTerm.Iri(Encoding.UTF8.GetBytes(graph)), cancellationToken).ConfigureAwait(false);
        }

        await using Endpoint endpoint = await store.EndpointAsync(cancellationToken).ConfigureAwait(false);
        return await RunRequestsAsync(endpoint, entry, [], cancellationToken).ConfigureAwait(false);
    }

    internal static void Mount(WebApplication app, ProtocolOptions options)
    {
        app.MapServiceDescription("/", options);
        app.MapSparqlEndpoint("/sparql", options);
        app.MapGraphStore("/" + ProtocolCatalogue.GraphStore, options);
    }

    private static async Task<Dictionary<string, string?>> RunSequenceAsync(ProtocolSubject subject, CancellationToken cancellationToken)
    {
        await using Store store = await Store.OpenAsync(subject, cancellationToken).ConfigureAwait(false);
        await using Endpoint endpoint = await store.EndpointAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> variables = [];
        Dictionary<string, string?> results = new(StringComparer.Ordinal);

        foreach (ProtocolEntry entry in ProtocolCatalogue.Entries.Where(e => e.Sequential))
        {
            results[entry.TestIri] = await RunRequestsAsync(endpoint, entry, variables, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private static async Task<string?> RunRequestsAsync(Endpoint endpoint, ProtocolEntry entry, Dictionary<string, string> variables, CancellationToken cancellationToken)
    {
        int index = 0;

        foreach (ProtocolRequest request in entry.Requests)
        {
            index++;
            string path = request.Path;

            foreach ((string name, string value) in variables)
            {
                // $LOCATION$ in a query string is an IRI to escape; $NEWPATH$ is a path.
                path = path.Replace("?graph=" + name, "?graph=" + Uri.EscapeDataString(value), StringComparison.Ordinal)
                    .Replace(name, new Uri(value).PathAndQuery, StringComparison.Ordinal);
            }

            path = endpoint.Rewrite(path);
            using HttpRequestMessage message = new(new HttpMethod(request.Method), new Uri(path.TrimStart('/'), UriKind.Relative));
            message.Headers.Host = ProtocolCatalogue.Host;

            if (request.Body is not null)
            {
                // The body names the store's address where the manifests do; on the server, the server's.
                message.Content = new ByteArrayContent(endpoint.RewriteText(Encoding.UTF8.GetString(request.Body)) is var text && text.Length == request.Body.Length ? request.Body : Encoding.UTF8.GetBytes(text));
            }

            foreach ((string name, string value) in request.Headers)
            {
                if (name.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                {
                    message.Content ??= new ByteArrayContent([]);
                    message.Content.Headers.TryAddWithoutValidation("Content-Type", value);
                }
                else if (!name.Equals("host", StringComparison.OrdinalIgnoreCase) && !name.Equals("content-length", StringComparison.OrdinalIgnoreCase))
                {
                    // Content-Length is the content's, computed; a transcript's is "...".
                    message.Headers.TryAddWithoutValidation(name, value);
                }
            }

            using HttpResponseMessage response = await endpoint.Client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            string where = "request " + index + " (" + request.Method + " " + request.Path + "): ";

            if (!request.Expect.Statuses.Any(s => s.Contains(status)))
            {
                return where + "status " + status + ", expected " + string.Join(" or ", request.Expect.Statuses) + "; " + Encoding.UTF8.GetString(body);
            }

            if (request.Expect.CapturesLocation)
            {
                if (response.Headers.Location is not { } location)
                {
                    return where + "no Location";
                }

                variables["$LOCATION$"] = location.ToString();
                variables["$NEWPATH$"] = location.ToString();
            }

            string? mediaType = response.Content.Headers.ContentType?.MediaType;

            if (request.Expect.ContentType is { } expectedType && status < 300
                && !string.Equals(mediaType, expectedType.Split(';')[0].Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return where + "Content-Type " + mediaType + ", expected " + expectedType;
            }

            ProtocolExpectation expect = request.Expect.Body is null ? request.Expect : request.Expect with { Body = endpoint.RewriteText(request.Expect.Body) };

            if (Check(expect, mediaType, body, path) is { } failure)
            {
                return where + failure;
            }
        }

        return null;
    }

    private static string? Check(ProtocolExpectation expect, string? mediaType, byte[] body, string path)
    {
        switch (expect.Format)
        {
            case "boolean":
            case "tabular":
                if (!ResultsFormat(mediaType, out SparqlResultsFormat format))
                {
                    return "a " + expect.Format + " result in " + mediaType;
                }

                SparqlResultsReader reader = new(body, format);

                if (!reader.ReadHead())
                {
                    return "the results do not parse: " + reader.Error;
                }

                if (expect.Format == "boolean")
                {
                    if (!reader.IsBoolean)
                    {
                        return "a solution sequence where a boolean was expected";
                    }

                    if (expect.Boolean is bool expected && reader.Boolean != expected)
                    {
                        return "ASK answered " + reader.Boolean + ", expected " + expected;
                    }
                }
                else if (reader.IsBoolean)
                {
                    return "a boolean where solutions were expected";
                }

                return null;

            case "RDF":
                return Graph(mediaType, body, "http://" + ProtocolCatalogue.Host + path, out _) is { } error ? error : null;
        }

        if (expect.Body is { } expected2 && body.Length > 0)
        {
            string baseIri = "http://" + ProtocolCatalogue.Host + path.Split('?')[0];

            if (Graph(mediaType, body, baseIri, out List<DataQuad>? actual) is { } error)
            {
                return error;
            }

            if (Graph("text/turtle", Encoding.UTF8.GetBytes(expected2), baseIri, out List<DataQuad>? wanted) is { } bad)
            {
                return "the manifest's expected body does not parse: " + bad;
            }

            return DatasetComparison.Compare(actual!, wanted!);
        }

        return null;
    }

    private static bool ResultsFormat(string? mediaType, out SparqlResultsFormat format)
    {
        (bool known, format) = mediaType switch
        {
            "application/sparql-results+json" or "application/json" => (true, SparqlResultsFormat.Json),
            "application/sparql-results+xml" or "application/xml" => (true, SparqlResultsFormat.Xml),
            "text/csv" => (true, SparqlResultsFormat.Csv),
            "text/tab-separated-values" => (true, SparqlResultsFormat.Tsv),
            _ => (false, default),
        };

        return known;
    }

    private static string? Graph(string? mediaType, byte[] body, string baseIri, out List<DataQuad>? quads)
    {
        List<DataQuad> parsed = [];
        quads = parsed;
        RdfSyntax syntax;

        switch (mediaType)
        {
            case "application/n-triples":
                syntax = RdfSyntax.NTriples;
                break;
            case "application/n-quads":
                syntax = RdfSyntax.NQuads;
                break;
            case "text/turtle":
                syntax = RdfSyntax.Turtle;
                break;
            case "application/trig":
                syntax = RdfSyntax.TriG;
                break;
            default:
                return "an RDF result in " + mediaType;
        }

        void Add(in QuadView quad) => parsed.Add(new DataQuad(
            quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise(), quad.HasGraph ? quad.Graph.Materialise() : null));

        ParseResult result = syntax is RdfSyntax.Turtle or RdfSyntax.TriG
            ? TurtleParser.Parse(body, Add, new TurtleOptions { Syntax = syntax, BaseIri = Encoding.UTF8.GetBytes(baseIri) })
            : NQuadsParser.Parse(body, Add, new ParseOptions { Syntax = syntax });
        return result.Succeeded ? null : "the RDF does not parse: " + result.FirstError;
    }

    /// <summary>
    /// A fresh dataset over the subject's backend, and what to clean up after
    /// it: a memory or file dataset in process, or the server's one dataset,
    /// deleted and created again through the admin API.
    /// </summary>
    internal sealed class Store : IAsyncDisposable
    {
        private readonly FileStorage? _files;
        private readonly string? _directory;
        private readonly HttpClient? _server;

        private Store(Dataset? dataset, FileStorage? files, string? directory, HttpClient? server)
        {
            InProcess = dataset;
            _files = files;
            _directory = directory;
            _server = server;
        }

        /// <summary>The in-process dataset, when the subject has one; the server's is reached over HTTP alone.</summary>
        internal Dataset? InProcess { get; }

        /// <summary>The in-process dataset.</summary>
        internal Dataset Dataset => InProcess ?? throw new InvalidOperationException("The server subject has no in-process dataset.");

        internal static async Task<Store> OpenAsync(ProtocolSubject subject, CancellationToken cancellationToken)
        {
            if (subject == ProtocolSubject.Server)
            {
                HttpClient server = new() { BaseAddress = ProtocolSubjects.ServerAddress ?? throw new InvalidOperationException(ProtocolSubjects.ServerVariable + " is not set.") };
                await RemoveAsync(server, cancellationToken).ConfigureAwait(false);
                using HttpResponseMessage created = await server.PutAsync("datasets/" + ProtocolSubjects.ServerDataset, new StringContent("{\"storage\":\"File\"}", Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);

                if (!created.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("The server did not create the dataset: " + (int)created.StatusCode + " " + await created.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                }

                return new Store(null, null, null, server);
            }

            DatasetOptions options = new() { Clock = FixedClock.Instance };

            if (subject == ProtocolSubject.Memory)
            {
                return new Store(await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false), null, null, null);
            }

            string directory = Path.Combine(Path.GetTempPath(), "varve-protocol", Guid.NewGuid().ToString("N"));
            FileStorage files = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = FixedClock.Instance }, cancellationToken).ConfigureAwait(false);
            return new Store(await Dataset.CreateAsync(files, new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false), files, directory, null);
        }

        /// <summary>The case's data into <paramref name="graph"/>: one commit in process, one Graph Store <c>PUT</c> on the server.</summary>
        internal async Task LoadAsync(IReadOnlyList<DataQuad> quads, RdfTerm graph, CancellationToken cancellationToken)
        {
            if (_server is null)
            {
                CommitRequest request = new();

                foreach (DataQuad quad in quads)
                {
                    request.Assert(quad.Subject, quad.Predicate, quad.Object, graph);
                }

                await Dataset.CommitAsync(request, cancellationToken).ConfigureAwait(false);
                return;
            }

            ArrayBufferWriter<byte> triples = new();
            WriteOptions options = new() { Syntax = RdfSyntax.NTriples };

            foreach (DataQuad quad in quads)
            {
                WriteTerm(triples, quad.Subject, options);
                triples.Write(" "u8);
                WriteTerm(triples, quad.Predicate, options);
                triples.Write(" "u8);
                WriteTerm(triples, quad.Object, options);
                triples.Write(" .\n"u8);
            }

            string target = "datasets/" + ProtocolSubjects.ServerDataset + "/graphs?graph=" + Uri.EscapeDataString(Encoding.UTF8.GetString(graph.Lexical));
            using ByteArrayContent body = new(triples.WrittenSpan.ToArray());
            body.Headers.TryAddWithoutValidation("Content-Type", "application/n-triples");
            using HttpResponseMessage response = await _server.PutAsync(target, body, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("The server did not load the case's data: " + (int)response.StatusCode + " " + await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
        }

        /// <summary>Where the case's requests go: a host started over the dataset, or the server.</summary>
        internal async Task<Endpoint> EndpointAsync(CancellationToken cancellationToken) =>
            _server is null
                ? Endpoint.InProcess(await ProtocolTestHost.StartAsync(Dataset, Mount, stopping: cancellationToken).ConfigureAwait(false))
                : Endpoint.Server(_server);

        public async ValueTask DisposeAsync()
        {
            if (_server is not null)
            {
                await RemoveAsync(_server, CancellationToken.None).ConfigureAwait(false);
                _server.Dispose();
                return;
            }

            await Dataset.DisposeAsync().ConfigureAwait(false);

            if (_files is not null)
            {
                await _files.DisposeAsync().ConfigureAwait(false);
                Directory.Delete(_directory!, recursive: true);
            }
        }

        // The dataset, closed then deleted (ADR 0106: an open dataset is not
        // deleted), so that the next case starts empty; nothing to remove
        // is fine.
        private static async Task RemoveAsync(HttpClient server, CancellationToken cancellationToken)
        {
            string dataset = "datasets/" + ProtocolSubjects.ServerDataset;
            using HttpResponseMessage closed = await server.PutAsync(dataset + "/state", new StringContent("{\"state\":\"closed\"}", Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);
            using HttpResponseMessage deleted = await server.DeleteAsync(dataset, cancellationToken).ConfigureAwait(false);

            if (!deleted.IsSuccessStatusCode && deleted.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException("The server did not delete the dataset: " + (int)deleted.StatusCode + " " + await deleted.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
        }

        private static void WriteTerm(ArrayBufferWriter<byte> output, RdfTerm term, in WriteOptions options)
        {
            int size = 256;

            while (true)
            {
                Span<byte> span = output.GetSpan(size);

                if (NQuadsWriter.TryWriteTerm(term, span, out int written, options))
                {
                    output.Advance(written);
                    return;
                }

                size *= 2;
            }
        }
    }
}
