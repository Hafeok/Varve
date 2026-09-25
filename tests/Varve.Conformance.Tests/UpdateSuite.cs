// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Iri;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Turtle;

namespace Varve.Conformance.Tests;

/// <summary>
/// A SPARQL 1.1 update evaluation suite (<c>sparql-update-store.md</c> §7):
/// the <c>UpdateEvaluationTest</c> entries of one manifest, each run over a
/// fresh in-memory store through <c>Varve.Sparql.Store</c>.
/// </summary>
internal sealed record UpdateSuite(string Id, string ManifestPath, string BaseIri)
{
    internal static IReadOnlyList<UpdateSuite> All { get; } =
    [
        .. new[]
        {
            "basic-update", "clear", "delete", "delete-data", "delete-insert", "delete-where", "drop", "add",
            "copy", "move", "update-silent",
        }.Select(d =>
        {
            string manifest = "sparql/sparql11/" + d + "/manifest.ttl";
            return new UpdateSuite("sparql11/" + d, manifest, EvaluationSuite.PublishedRoot + manifest);
        }),
    ];
}

/// <summary>A dataset as a manifest states it: a default graph file, and named graph files by name.</summary>
internal sealed record UpdateDataset(IReadOnlyList<string> Data, IReadOnlyList<(string File, string Graph)> GraphData);

/// <summary>One update evaluation case.</summary>
internal sealed record UpdateEntry(string TestIri, string Suite, string Name, string RequestIri, UpdateDataset Before, UpdateDataset After);

/// <summary>The update cases of every wired suite, read once.</summary>
internal static class UpdateCatalogue
{
    private const string Mf = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#";
    private const string Ut = "http://www.w3.org/2009/sparql/tests/test-update#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";

    private static readonly Lazy<IReadOnlyList<UpdateEntry>> AllEntries = new(ReadAll);
    private static readonly Lazy<IReadOnlyDictionary<string, UpdateEntry>> Index = new(() => AllEntries.Value.ToDictionary(e => e.TestIri, StringComparer.Ordinal));

    internal static IReadOnlyList<UpdateEntry> Entries => AllEntries.Value;

    internal static IReadOnlyDictionary<string, UpdateEntry> ByIri => Index.Value;

    internal static IReadOnlyList<UpdateEntry> Of(UpdateSuite suite) =>
        [.. Entries.Where(e => string.Equals(e.Suite, suite.Id, StringComparison.Ordinal))];

    private static List<UpdateEntry> ReadAll()
    {
        List<UpdateEntry> all = [];

        foreach (UpdateSuite suite in UpdateSuite.All)
        {
            ManifestGraph graph = ManifestGraph.Load(TestData.ResolveFromRoot(suite.ManifestPath), suite.BaseIri);
            RdfTerm manifest = graph.Subjects(Rdf + "type", Mf + "Manifest").Single();

            foreach (RdfTerm list in graph.Objects(manifest, Mf + "entries"))
            {
                foreach (RdfTerm entry in graph.Collection(list))
                {
                    if (graph.Object(entry, Rdf + "type") is not { } type
                        || !string.Equals(ManifestGraph.Text(type), Mf + "UpdateEvaluationTest", StringComparison.Ordinal)
                        || graph.Object(entry, Mf + "action") is not { } action
                        || graph.Object(entry, Mf + "result") is not { } result)
                    {
                        continue;
                    }

                    string testIri = ManifestGraph.Text(entry);
                    all.Add(new UpdateEntry(
                        testIri,
                        suite.Id,
                        graph.Object(entry, Mf + "name") is { } name ? ManifestGraph.Text(name) : testIri,
                        ManifestGraph.Text(graph.Object(action, Ut + "request")!),
                        DatasetOf(graph, action),
                        DatasetOf(graph, result)));
                }
            }
        }

        return all;

        static UpdateDataset DatasetOf(ManifestGraph graph, RdfTerm node) => new(
            [.. graph.Objects(node, Ut + "data").Select(ManifestGraph.Text)],
            [.. graph.Objects(node, Ut + "graphData").Select(g => (
                ManifestGraph.Text(graph.Object(g, Ut + "graph")!),
                ManifestGraph.Text(graph.Object(g, Rdfs + "label")!)))]);
    }
}

/// <summary>What one update case came to: the failure, if any, and the commit count it made.</summary>
internal sealed record UpdateOutcome(string? Failure, CommitOutcome Result, long Commits);

internal static class UpdateRunner
{
    /// <summary>
    /// Loads the action's dataset as setup commits, executes the request, and
    /// compares the store's read at the head with the result's dataset.
    /// Asserts on the way that the request made exactly one commit when it
    /// changed something and none when it did not (ADR 0057).
    /// </summary>
    internal static async Task<UpdateOutcome> RunAsync(UpdateEntry entry, CancellationToken cancellationToken)
    {
        await using Dataset dataset = await Dataset.OpenAsync(new MemoryStorage(), new DatasetOptions { Clock = FixedClock.Instance }, cancellationToken);

        foreach ((IReadOnlyList<DataQuad> quads, RdfTerm? graph) in Files(entry.Before))
        {
            await LoadAsync(dataset, quads, graph, cancellationToken);
        }

        long before = dataset.Head;
        byte[] text = File.ReadAllBytes(EvaluationSuite.PathOf(entry.RequestIri));
        Update update = SparqlParser.ParseUpdate(text, new SparqlParseOptions(Encoding.UTF8.GetBytes(entry.RequestIri), SparqlVersion.Sparql11));
        UpdateOptions options = new()
        {
            Evaluation = new EvaluationOptions { Clock = FixedClock.Instance, Randomness = new SeededRandom() },
            LoadSource = new SuiteLoadSource(),
        };

        CommitResult result;

        try
        {
            result = await SparqlUpdate.ExecuteAsync(dataset, update, options, cancellationToken);
        }
        catch (SparqlUpdateException error)
        {
            return new UpdateOutcome("the request failed: " + error.Message, CommitOutcome.Rejected, 0);
        }

        long commits = dataset.Head - before;
        string? commitFailure = result.Outcome switch
        {
            CommitOutcome.Committed when commits != 1 => "Committed, but the log grew by " + commits + " commit(s)",
            CommitOutcome.Committed when (await dataset.DiffAsync(before, dataset.Head, cancellationToken)).IsEmpty => "Committed an empty delta",
            CommitOutcome.NoChange when commits != 0 => "NoChange, but the log grew by " + commits + " commit(s)",
            CommitOutcome.Committed or CommitOutcome.NoChange => null,
            _ => "the request returned " + result.Outcome,
        };

        if (commitFailure is not null)
        {
            return new UpdateOutcome(commitFailure, result.Outcome, commits);
        }

        List<DataQuad> actual = [];
        using (DatasetView view = await dataset.AsOfAsync(dataset.Head, cancellationToken))
        using (IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
        {
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                actual.Add(new DataQuad(
                    Term(view, quad.Subject),
                    Term(view, quad.Predicate),
                    Term(view, quad.Object),
                    quad.Graph.IsNone ? null : Term(view, quad.Graph)));
            }
        }

        // Each expected file's blank nodes are its own, as separate loads keep them.
        List<DataQuad> expected = [];
        int file = 0;
        foreach ((IReadOnlyList<DataQuad> quads, RdfTerm? graph) in Files(entry.After))
        {
            expected.AddRange(DatasetComparison.Apart(quads.Select(q => q with { Graph = graph ?? q.Graph }), file++));
        }

        string? failure = DatasetComparison.Compare(actual, expected);
        return new UpdateOutcome(failure, result.Outcome, commits);
    }

    private static RdfTerm Term(DatasetView source, TermHandle handle) =>
        source.TryExternalise(handle, out RdfTerm? term) ? term : throw new InvalidOperationException("A handle the view cannot externalise.");

    private static IEnumerable<(IReadOnlyList<DataQuad>, RdfTerm?)> Files(UpdateDataset dataset)
    {
        foreach (string iri in dataset.Data)
        {
            yield return (EvaluationData.Quads(iri), null);
        }

        foreach ((string file, string graph) in dataset.GraphData)
        {
            yield return (EvaluationData.Quads(file), RdfTerm.Iri(Encoding.UTF8.GetBytes(graph)));
        }
    }

    // One setup commit per file, so that each file's blank nodes are its own.
    private static async Task LoadAsync(Dataset dataset, IReadOnlyList<DataQuad> quads, RdfTerm? graph, CancellationToken cancellationToken)
    {
        CommitRequest request = new();

        foreach (DataQuad quad in quads)
        {
            RdfTerm? g = graph ?? quad.Graph;

            if (g is null)
            {
                request.Assert(quad.Subject, quad.Predicate, quad.Object);
            }
            else
            {
                request.Assert(quad.Subject, quad.Predicate, quad.Object, g);
            }
        }

        if (request.Count > 0)
        {
            await dataset.CommitAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// <c>LOAD</c> in the harness: <c>file:</c> IRIs and the suites' published
    /// IRIs, from the submodule; anything else fails, which is what
    /// <c>update-silent</c>'s <c>somescheme:</c> IRIs need. The IRI is taken
    /// apart by <c>Varve.Iri</c>, not <c>System.Uri</c> (ADR 0004).
    /// </summary>
    internal sealed class SuiteLoadSource : ILoadSource
    {
        public ValueTask<LoadedDocument> LoadAsync(RdfTerm iri, CancellationToken cancellationToken)
        {
            string text = Encoding.UTF8.GetString(iri.Lexical);
            string? path = null;

            if (text.StartsWith(EvaluationSuite.PublishedRoot, StringComparison.Ordinal))
            {
                path = EvaluationSuite.PathOf(text);
            }
            else if (IriRef.TryValidate(iri.Lexical, out IriComponents parts, out _)
                && parts.HasScheme
                && iri.Lexical[parts.Scheme].SequenceEqual("file"u8))
            {
                path = PercentDecoded(iri.Lexical[parts.Path]);
            }

            if (path is null || !File.Exists(path) || !Path.GetFullPath(path).StartsWith(Path.GetFullPath(TestData.RdfTestsRoot), StringComparison.Ordinal))
            {
                return ValueTask.FromResult(LoadedDocument.Failed("not a file of the test suites: " + text));
            }

            RdfSyntax syntax = Path.GetExtension(path) switch
            {
                ".nt" => RdfSyntax.NTriples,
                ".nq" => RdfSyntax.NQuads,
                ".trig" => RdfSyntax.TriG,
                _ => RdfSyntax.Turtle,
            };

            return ValueTask.FromResult(LoadedDocument.Of(File.ReadAllBytes(path), syntax, iri.Lexical.ToArray()));
        }

        // RFC 3986 §2.1, over the path's UTF-8.
        private static string PercentDecoded(ReadOnlySpan<byte> path)
        {
            List<byte> bytes = new(path.Length);

            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] == (byte)'%' && i + 2 < path.Length
                    && byte.TryParse(Encoding.ASCII.GetString(path.Slice(i + 1, 2)), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte value))
                {
                    bytes.Add(value);
                    i += 2;
                }
                else
                {
                    bytes.Add(path[i]);
                }
            }

            return Encoding.UTF8.GetString([.. bytes]);
        }
    }
}
