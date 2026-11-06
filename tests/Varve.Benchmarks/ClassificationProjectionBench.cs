// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Varve.Classification;
using Varve.Classification.Model;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Store;
using Varve.Store.Log;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// <c>--classification-projection &lt;S|M&gt;</c> (ADR 0233): the
/// classification projection of a classifier binding over the REC
/// portfolio's site graphs, on the file backend, over the portfolio's history
/// but its last <see cref="Held"/> commits, as
/// <see cref="ValidationProjectionBench"/> measures the validation projection.
/// </summary>
/// <remarks>
/// <para>
/// The classifier's shapes make every <c>rec:Company</c> (a tenant, or a
/// site's owner) and every <c>rec:Site</c> a data subject. A lease belongs to
/// its tenant and its leasor by <c>rec:leasee</c> and <c>rec:leasor</c>; a
/// level, a building and a room belong to their site up <c>rec:isPartOf</c>,
/// and an asset to its room's by <c>rec:locatedIn</c>. Points are reached by
/// <c>rec:hasPoint</c> from their host only, the wrong way for a link, so they
/// stay unclassified. <c>rdf:type</c>, the portfolio's one vocabulary-valued
/// predicate, is public.
/// </para>
/// <para>
/// Three measurements, as the validation projection's: a cold start, a
/// generation at the head; the lag of each held commit; and a rebuild for a
/// shapes change, a commit adding or removing <c>rec:leaseOf</c> as a link,
/// <see cref="ShapesChanges"/> times. Then the state saved to a derived
/// store, its size, and the time to read it back.
/// </para>
/// </remarks>
internal static class ClassificationProjectionBench
{
    private const int Held = 200;
    private const int ShapesChanges = 3;
    private const string Rec = "https://w3id.org/rec#";
    private const string Sh = "http://www.w3.org/ns/shacl#";
    private const string VarveNs = "https://w3id.org/varve/ns#";
    private const string Shapes = "https://example.org/rec/classifier/";

    private static readonly RdfTerm ShapesGraphIri = Iri("https://example.org/rec/classifier");
    private static readonly RdfTerm Type = Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type");
    private static readonly RdfTerm True = RdfTerm.Literal("true"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#boolean"u8));

    internal static async Task RunAsync(string sizeName)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        (FileDataset dataset, List<RecChange> held) = await RecShapes.LoadAsync(size, 20261015, Held);
        await using (dataset)
        {
            await dataset.Dataset.CommitAsync(ClassifierShapes());
            ClassifierBinding binding = RecBinding;
            ClassificationProjection projection = new(dataset.Dataset, binding);

            Stopwatch clock = Stopwatch.StartNew();
            using (DatasetView head = dataset.Dataset.Pin())
            {
                await projection.ResetAsync(head, default);
            }

            TimeSpan cold = clock.Elapsed;
            Report(projection, "cold start at the head");
            string counts = Count(dataset.Dataset, projection);
            Console.WriteLine("// " + counts);

            List<double> lag = [];
            int changedNodes = 0;
            await using IAsyncEnumerator<Commit> feed = dataset.Dataset.Subscribe(dataset.Dataset.Head, SubscriptionFilter.All).GetAsyncEnumerator();
            foreach (RecChange change in held)
            {
                await dataset.Dataset.CommitAsync(change.Request([]));
                await feed.MoveNextAsync();
                long started = Stopwatch.GetTimestamp();
                await projection.ApplyAsync(feed.Current, default);
                lag.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                changedNodes += projection.ChangedAt(feed.Current.Position).Count;
            }

            List<double> rebuilds = [];
            List<int> reclassified = [];
            RdfTerm leaseOf = Iri(Shapes + "LeaseOf");
            for (int i = 0; i < ShapesChanges; i++)
            {
                CommitRequestBuilder change = new();
                (RdfTerm, RdfTerm, RdfTerm)[] triples =
                [
                    (leaseOf, Type, Iri(Sh + "PropertyShape")),
                    (leaseOf, Iri(Sh + "path"), Iri(Rec + "leaseOf")),
                    (leaseOf, Iri(VarveNs + "subjectLink"), True),
                ];
                foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in triples)
                {
                    if (i % 2 == 0)
                    {
                        change.Assert(s, p, o, ShapesGraphIri);
                    }
                    else
                    {
                        change.Retract(s, p, o, ShapesGraphIri);
                    }
                }

                await dataset.Dataset.CommitAsync(change);
                await feed.MoveNextAsync();
                long started = Stopwatch.GetTimestamp();
                await projection.ApplyAsync(feed.Current, default);
                rebuilds.Add(Stopwatch.GetElapsedTime(started).TotalSeconds);
                reclassified.Add(projection.ChangedAt(feed.Current.Position).Count);
            }

            Report(projection, "after the shapes changes");

            // The file dataset's own local cache, as the server saves to it.
            IDerivedStore cache = dataset.Storage.Derived;
            long savedAt = Stopwatch.GetTimestamp();
            await projection.SaveAsync(cache, default);
            double save = Stopwatch.GetElapsedTime(savedAt).TotalMilliseconds;
            long bytes = 0;
            foreach (BlobName name in await cache.ListAsync(default))
            {
                if (name.Value.StartsWith("classification/", StringComparison.Ordinal))
                {
                    using IReadableBlob blob = await cache.OpenAsync(name, default);
                    bytes += blob.Length.Value;
                }
            }

            ClassificationProjection loaded = new(dataset.Dataset, binding);
            long loadedAt = Stopwatch.GetTimestamp();
            bool read = await loaded.LoadAsync(cache, default);
            double load = Stopwatch.GetElapsedTime(loadedAt).TotalMilliseconds;

            lag.Sort();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"REC {size} (generator output version {RecPortfolio.Version}), {dataset.Dataset.Head.Value:N0} commits:"));
            Console.WriteLine("  at the head after the cold start: " + counts);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  cold start, a generation at the head: {cold.TotalSeconds:F2} s"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  rebuild for a shapes change: mean {rebuilds.Average():F2} s, min {rebuilds.Min():F2} s, max {rebuilds.Max():F2} s over {rebuilds.Count}; nodes reclassified {string.Join(", ", reclassified)}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  lag per commit, {lag.Count} commits: mean {lag.Average():F2} ms, median {lag[lag.Count / 2]:F2} ms, p95 {lag[(int)(lag.Count * 0.95)]:F2} ms, max {lag[^1]:F2} ms; {changedNodes:N0} nodes reclassified"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  saved state: {bytes:N0} bytes, written in {save:F1} ms, read back in {load:F1} ms ({(read ? "read" : "a miss")}, at {loaded.Position.Value:N0})"));
        }
    }

    // The rows of the classified graphs at the head (their subject nodes), the
    // classified among them (a non-empty subject set) and the data subjects
    // (each in its own set).
    private static string Count(StoreDatasetType dataset, ClassificationProjection projection)
    {
        using DatasetView view = dataset.Pin();
        projection.TryAt(view.Position, view, out IRowClassification rows);
        HashSet<TermHandle> nodes = new(view.TermComparer);
        using (IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
        {
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                if (!rows.BindingOf(quad.Graph).IsNone)
                {
                    nodes.Add(quad.Subject);
                }
            }
        }

        BindingIndex only = new(0);
        int classified = 0;
        int subjects = 0;
        HashSet<SubjectSetId> sets = [];
        foreach (TermHandle node in nodes)
        {
            SubjectSetId set = rows.SubjectsOf(only, node);
            if (set.IsNone)
            {
                continue;
            }

            classified++;
            sets.Add(set);
            foreach (TermHandle member in rows.Members(only, set))
            {
                if (view.TermComparer.Equals(member, node))
                {
                    subjects++;
                    break;
                }
            }
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"{nodes.Count:N0} rows in the site graphs, {classified:N0} classified, {subjects:N0} data subjects, {sets.Count:N0} distinct subject sets");
    }

    /// <summary>The classifier binding over REC's site graphs.</summary>
    internal static ClassifierBinding RecBinding { get; } = new(Iri("https://example.org/rec/rows"), ShapesGraphIri, GraphScope.Of([], ["https://example.org/rec/graph/"], DefaultGraphAccess.Excluded));

    /// <summary>The annotated shapes: companies and sites are data subjects; leases, topology and assets belong to them.</summary>
    internal static CommitRequestBuilder ClassifierShapes()
    {
        CommitRequestBuilder shapes = new();
        Subject(shapes, "Company", "Company");
        Subject(shapes, "Site", "Site");
        Link(shapes, "Leasee", "leasee", "subjectLink");
        Link(shapes, "Leasor", "leasor", "subjectLink");
        Link(shapes, "PartOf", "isPartOf", "subjectLink");
        Link(shapes, "LocatedIn", "locatedIn", "subjectLink");
        shapes.Assert(Iri(Shapes + "Type"), Type, Iri(Sh + "PropertyShape"), ShapesGraphIri)
            .Assert(Iri(Shapes + "Type"), Iri(Sh + "path"), Type, ShapesGraphIri)
            .Assert(Iri(Shapes + "Type"), Iri(VarveNs + "publicLink"), True, ShapesGraphIri);
        return shapes;
    }

    private static void Subject(CommitRequestBuilder shapes, string name, string @class)
    {
        RdfTerm shape = Iri(Shapes + name);
        shapes.Assert(shape, Type, Iri(Sh + "NodeShape"), ShapesGraphIri)
            .Assert(shape, Iri(Sh + "targetClass"), Iri(Rec + @class), ShapesGraphIri)
            .Assert(shape, Iri(VarveNs + "dataSubject"), True, ShapesGraphIri);
    }

    private static void Link(CommitRequestBuilder shapes, string name, string predicate, string kind)
    {
        RdfTerm shape = Iri(Shapes + name);
        shapes.Assert(shape, Type, Iri(Sh + "PropertyShape"), ShapesGraphIri)
            .Assert(shape, Iri(Sh + "path"), Iri(Rec + predicate), ShapesGraphIri)
            .Assert(shape, Iri(VarveNs + kind), True, ShapesGraphIri);
    }

    private static void Report(ClassificationProjection projection, string when) =>
        Console.WriteLine("// " + when + ": position " + projection.Position.Value.ToString(CultureInfo.InvariantCulture)
            + ", " + projection.Problems.Count.ToString(CultureInfo.InvariantCulture) + " problems");

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));
}
