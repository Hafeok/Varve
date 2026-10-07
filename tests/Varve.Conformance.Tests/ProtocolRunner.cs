// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
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

/// <summary>The store a protocol case runs over: the memory backend, or the file backend in a fresh directory.</summary>
internal enum ProtocolSubject
{
    Memory,
    File,
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
            await LoadAsync(store.Dataset, EvaluationData.Quads(file), RdfTerm.Iri(Encoding.UTF8.GetBytes(graph)), cancellationToken).ConfigureAwait(false);
        }

        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(store.Dataset, Mount, stopping: cancellationToken).ConfigureAwait(false);
        return await RunRequestsAsync(host, entry, [], cancellationToken).ConfigureAwait(false);
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
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(store.Dataset, Mount, stopping: cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> variables = [];
        Dictionary<string, string?> results = new(StringComparer.Ordinal);

        foreach (ProtocolEntry entry in ProtocolCatalogue.Entries.Where(e => e.Sequential))
        {
            results[entry.TestIri] = await RunRequestsAsync(host, entry, variables, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private static async Task<string?> RunRequestsAsync(ProtocolTestHost host, ProtocolEntry entry, Dictionary<string, string> variables, CancellationToken cancellationToken)
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

            using HttpRequestMessage message = new(new HttpMethod(request.Method), new Uri(path.TrimStart('/'), UriKind.Relative));
            message.Headers.Host = ProtocolCatalogue.Host;

            if (request.Body is not null)
            {
                message.Content = new ByteArrayContent(request.Body);
            }

            foreach ((string name, string value) in request.Headers)
            {
                if (name.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                {
                    message.Content ??= new ByteArrayContent([]);
                    message.Content.Headers.TryAddWithoutValidation("Content-Type", value);
                }
                else if (!name.Equals("host", StringComparison.OrdinalIgnoreCase))
                {
                    message.Headers.TryAddWithoutValidation(name, value);
                }
            }

            using HttpResponseMessage response = await host.Client.SendAsync(message, cancellationToken).ConfigureAwait(false);
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

            if (Check(request.Expect, mediaType, body, path) is { } failure)
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

    private static async Task LoadAsync(Dataset dataset, IReadOnlyList<DataQuad> quads, RdfTerm graph, CancellationToken cancellationToken)
    {
        CommitRequest request = new();

        foreach (DataQuad quad in quads)
        {
            request.Assert(quad.Subject, quad.Predicate, quad.Object, graph);
        }

        await dataset.CommitAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A fresh dataset over the subject's backend, and what to clean up after it.</summary>
    internal sealed class Store : IAsyncDisposable
    {
        private readonly FileStorage? _files;
        private readonly string? _directory;

        private Store(Dataset dataset, FileStorage? files, string? directory)
        {
            Dataset = dataset;
            _files = files;
            _directory = directory;
        }

        internal Dataset Dataset { get; }

        internal static async Task<Store> OpenAsync(ProtocolSubject subject, CancellationToken cancellationToken)
        {
            DatasetOptions options = new() { Clock = FixedClock.Instance };

            if (subject == ProtocolSubject.Memory)
            {
                return new Store(await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false), null, null);
            }

            string directory = Path.Combine(Path.GetTempPath(), "varve-protocol", Guid.NewGuid().ToString("N"));
            FileStorage files = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = FixedClock.Instance }, cancellationToken).ConfigureAwait(false);
            return new Store(await Dataset.CreateAsync(files, new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false), files, directory);
        }

        public async ValueTask DisposeAsync()
        {
            await Dataset.DisposeAsync().ConfigureAwait(false);

            if (_files is not null)
            {
                await _files.DisposeAsync().ConfigureAwait(false);
                Directory.Delete(_directory!, recursive: true);
            }
        }
    }
}
