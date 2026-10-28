// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Shacl;
using Varve.Shacl.Sparql;
using Varve.Shacl.Store;
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Benchmarks;

/// <summary>
/// <c>--validation-projection &lt;S|M&gt;</c> (ADR 0174): the validation
/// projection of a <c>report</c> binding of REC 4.1.0's shapes in the REC
/// gate's composition (ADR 0176), on the file backend, over the portfolio's
/// history but its last <see cref="Held"/> commits.
/// </summary>
/// <remarks>
/// Three measurements, each of a projection that keeps up with its dataset:
/// <list type="bullet">
/// <item>a cold start, a generation at the head (ADR 0174 point 7);</item>
/// <item>its lag: the time to apply each held commit once it lands, which is
/// how far the report graph trails the head;</item>
/// <item>a rebuild for a shapes change: a commit that adds or removes one
/// shape, and the generation it starts, <see cref="ShapesChanges"/> times.</item>
/// </list>
/// Not BenchmarkDotNet: a rebuild at M is a full validation of five million
/// quads, which is minutes, not microseconds, and each measurement is one
/// run. The portfolio's generator version is printed with the results.
/// </remarks>
internal static class ValidationProjectionBench
{
    private const int Held = 200;
    private const int ShapesChanges = 3;

    private static readonly RdfTerm Extra = RdfTerm.Iri("https://example.org/rec/shapes/extra"u8);

    internal static async Task RunAsync(string sizeName)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        (FileDataset dataset, List<RecChange> held) = await RecShapes.LoadAsync(size, 20261015, Held, composed: true);
        await using (dataset)
        {
            ShaclOptions options = new() { Extensions = [SparqlConstraints.Extension()] };
            ShapesBinding binding = new(RdfTerm.Iri("https://example.org/rec/binding"u8), RecShapes.ShapesGraphIri, GraphScope.Of([], ["https://example.org/rec/graph/"], DefaultGraphAccess.Excluded), ValidationMode.Report) { SubClassOfInShapesGraph = true };
            ValidationProjection projection = new(dataset.Dataset, binding, options);

            Stopwatch clock = Stopwatch.StartNew();
            using (DatasetView head = dataset.Dataset.Pin())
            {
                await projection.ResetAsync(head, default);
            }

            TimeSpan cold = clock.Elapsed;
            Report(projection, "cold start at the head");

            List<double> lag = [];
            await using IAsyncEnumerator<Commit> feed = dataset.Dataset.Subscribe(dataset.Dataset.Head, SubscriptionFilter.All).GetAsyncEnumerator();
            foreach (RecChange change in held)
            {
                await dataset.Dataset.CommitAsync(change.Request([]));
                await feed.MoveNextAsync();
                long started = Stopwatch.GetTimestamp();
                await projection.ApplyAsync(feed.Current, default);
                lag.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            List<double> rebuilds = [];
            for (int i = 0; i < ShapesChanges; i++)
            {
                CommitRequestBuilder change = new();
                RdfTerm type = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);
                RdfTerm nodeShape = RdfTerm.Iri("http://www.w3.org/ns/shacl#NodeShape"u8);
                RdfTerm targetNode = RdfTerm.Iri("http://www.w3.org/ns/shacl#targetNode"u8);
                RdfTerm node = RdfTerm.Iri("https://example.org/rec/site/0"u8);
                if (i % 2 == 0)
                {
                    change.Assert(Extra, type, nodeShape, RecShapes.ShapesGraphIri).Assert(Extra, targetNode, node, RecShapes.ShapesGraphIri);
                }
                else
                {
                    change.Retract(Extra, type, nodeShape, RecShapes.ShapesGraphIri).Retract(Extra, targetNode, node, RecShapes.ShapesGraphIri);
                }

                await dataset.Dataset.CommitAsync(change);
                await feed.MoveNextAsync();
                long started = Stopwatch.GetTimestamp();
                await projection.ApplyAsync(feed.Current, default);
                rebuilds.Add(Stopwatch.GetElapsedTime(started).TotalSeconds);
            }

            Report(projection, "after the shapes changes");
            lag.Sort();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"REC {size} (generator output version {RecPortfolio.Version}), {dataset.Dataset.Head.Value:N0} commits:"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  cold start, a generation at the head: {cold.TotalSeconds:F1} s"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  rebuild for a shapes change: mean {rebuilds.Average():F1} s, min {rebuilds.Min():F1} s, max {rebuilds.Max():F1} s over {rebuilds.Count}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  lag per commit, {lag.Count} commits: mean {lag.Average():F2} ms, median {lag[lag.Count / 2]:F2} ms, p95 {lag[(int)(lag.Count * 0.95)]:F2} ms, max {lag[^1]:F2} ms"));
        }
    }

    private static void Report(ValidationProjection projection, string when)
    {
        projection.TryReportAt(projection.Position, out ValidationOutcome outcome);
        Console.WriteLine("// " + when + ": position " + projection.Position.Value.ToString(CultureInfo.InvariantCulture)
            + (outcome.Report is { } report ? ", " + report.Results.Count.ToString(CultureInfo.InvariantCulture) + " results" : ", " + outcome.Problems.Count.ToString(CultureInfo.InvariantCulture) + " problems"));
    }
}
