// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Shacl;
using Varve.Shacl.Store;
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;

namespace Varve.Benchmarks;

/// <summary>
/// Commit latency under a <c>reject</c> shapes binding against none (ADR
/// 0169), on the file backend: the REC portfolio (ADR 0141) at S or M, its
/// history but the last <see cref="Held"/> commits loaded, REC 4.1.0's shapes
/// in a named graph of the dataset, and the held commits committed one at a
/// time, with the binding's gate as the request's validator or with none.
/// </summary>
/// <remarks>
/// <para>
/// The shapes are REC's three files as a merge, with
/// <c>owl:Class rdfs:subClassOf rdfs:Class</c> added (the maintainer's ruling
/// on #13) and without Brick's three <c>sh:sparql</c> constraints, whose
/// prefixes the vendored files never declare, so they refuse the whole shapes
/// graph (reported on #138). The binding validates the site graphs, with
/// <c>varve:subClassOfInShapesGraph</c>, since the class hierarchy is the
/// shapes graph's.
/// </para>
/// <para>
/// The portfolio is the generator's output version 2
/// (<see cref="RecPortfolio.Version"/>), whose head conforms to these shapes,
/// so every held commit is accepted under the gate; the refusals, printed at
/// the end, are a check that none was refused. The shapes are loaded by one
/// commit through the gate before anything is timed.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[WarmupCount(1)]
[IterationCount(8)]
[InvocationCount(1, 1)]
public class ShapesGateBenchmarks
{
    internal const int Batch = 20;
    internal const int Held = 12 * Batch;
    private const ulong Seed = 20261015;

    private readonly Queue<CommitRequestBuilder> _held = new();
    private FileDataset? _dataset;
    private ShapesGate? _gate;
    private int _refused;
    private int _committed;

    [Params("S", "M")]
    public string Size { get; set; } = "S";

    [Params("none", "reject")]
    public string Binding { get; set; } = "none";

    [GlobalSetup]
    public void Setup()
    {
        (_dataset, List<RecChange> held) = RecShapes.LoadAsync(Enum.Parse<RecSize>(Size), Seed, Held + 1).GetAwaiter().GetResult();
        _gate = Binding == "reject" ? RecShapes.Gate() : null;
        ICommitValidator[] validators = _gate is null ? [] : [_gate];
        foreach (RecChange change in held.Skip(1))
        {
            _held.Enqueue(change.Request(validators));
        }

        // The shapes loaded, outside the measurement.
        Stopwatch first = Stopwatch.StartNew();
        CommitResult result = _dataset.Dataset.CommitAsync(held[0].Request(validators)).AsTask().GetAwaiter().GetResult();
        Console.WriteLine("// first commit " + result.Outcome + " in " + first.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine("// " + Binding + " at " + Size + ": " + _committed + " committed, " + _refused + " refused");
        _dataset!.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary><see cref="Batch"/> commits of the portfolio's history, each flushed.</summary>
    [Benchmark(OperationsPerInvoke = Batch)]
    public async Task<long> Commit()
    {
        long position = 0;
        for (int i = 0; i < Batch; i++)
        {
            CommitResult result = await _dataset!.Dataset.CommitAsync(_held.Dequeue());
            if (result.Outcome == CommitOutcome.Rejected)
            {
                _refused++;
            }
            else
            {
                _committed++;
                position = result.Position.Value;
            }
        }

        return position;
    }
}

/// <summary>
/// REC 4.1.0's shapes for <see cref="ShapesGateBenchmarks"/>, and
/// <c>--shapes-gate-affected &lt;S|M&gt;</c>: the same held commits through the
/// gate, each one's time in the gate and the focus nodes it affects, for the
/// overhead per affected focus node.
/// </summary>
internal static class RecShapes
{
    private const string Rec = "https://w3id.org/rec#";
    internal static readonly RdfTerm ShapesGraphIri = RdfTerm.Iri("https://example.org/rec/shapes"u8);
    private static readonly RdfTerm BindingIri = RdfTerm.Iri("https://example.org/rec/binding"u8);
    private static readonly RdfTerm Sparql = RdfTerm.Iri("http://www.w3.org/ns/shacl#sparql"u8);

    internal static ShapesGate Gate() => new([Binding()]);

    internal static ShapesBinding Binding() =>
        new(BindingIri, ShapesGraphIri, GraphScope.Of([], ["https://example.org/rec/graph/"], DefaultGraphAccess.Excluded), ValidationMode.Reject) { SubClassOfInShapesGraph = true };

    /// <summary>
    /// The shapes' triples: REC's three files merged, each file's blank nodes
    /// its own. Without <paramref name="composed"/>, part 2a's reading: no
    /// SPARQL constraint, and the <c>owl:Class</c> triple. With it, the REC
    /// gate's composition (ADR 0176): every constraint, and
    /// <c>testdata/rec/gate/composition.ttl</c>.
    /// </summary>
    internal static List<(RdfTerm S, RdfTerm P, RdfTerm O)> Triples(bool composed = false)
    {
        List<(RdfTerm, RdfTerm, RdfTerm)> triples = [];
        string directory = Path.Combine(Root(), "testdata", "rec", "v4.1.0");
        List<string> files = [Path.Combine(directory, "rec.ttl"), Path.Combine(directory, "Brick+patches.ttl"), Path.Combine(directory, "recimports.ttl")];
        if (composed)
        {
            files.Add(Path.Combine(Root(), "testdata", "rec", "gate", "composition.ttl"));
        }

        for (int f = 0; f < files.Count; f++)
        {
            string prefix = "f" + f.ToString(CultureInfo.InvariantCulture) + "_";
            RdfTerm Own(RdfTerm term) => term.Kind == RdfTermKind.BlankNode ? RdfTerm.BlankNode(Encoding.UTF8.GetBytes(prefix + Encoding.UTF8.GetString(term.Lexical))) : term;
            ParseResult parsed = TurtleParser.Parse(File.ReadAllBytes(files[f]), (in QuadView q) =>
            {
                RdfTerm predicate = q.Predicate.Materialise();
                if (composed || !predicate.Equals(Sparql))
                {
                    triples.Add((Own(q.Subject.Materialise()), predicate, Own(q.Object.Materialise())));
                }
            }, new TurtleOptions { Syntax = RdfSyntax.Turtle });
            if (!parsed.Succeeded)
            {
                throw new InvalidOperationException(files[f] + ": " + parsed.FirstError);
            }
        }

        if (!composed)
        {
            triples.Add((RdfTerm.Iri("http://www.w3.org/2002/07/owl#Class"u8), RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#subClassOf"u8), RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#Class"u8)));
        }

        return triples;
    }

    /// <summary>A file-backed dataset with the portfolio's history but its last <paramref name="held"/> commits, and the shapes; the held commits as requests.</summary>
    internal static async Task<(FileDataset Dataset, List<RecChange> Held)> LoadAsync(RecSize size, ulong seed, int held, bool composed = false)
    {
        FileDataset dataset = await FileDataset.CreateAsync();
        Dictionary<string, RdfTerm> terms = new(StringComparer.Ordinal);
        RdfTerm Of(string text)
        {
            if (!terms.TryGetValue(text, out RdfTerm? term))
            {
                term = text[0] == '<' ? RdfTerm.Iri(Encoding.UTF8.GetBytes(text[1..^1])) : RdfTerm.Literal(Encoding.UTF8.GetBytes(text[1..^1]));
                terms[text] = term;
            }

            return term;
        }

        int loaded = RecPortfolio.Shape(size).Commits - held;
        List<RecChange> requests = [];
        Stopwatch clock = Stopwatch.StartNew();
        int n = 0;
        foreach (RecCommit commit in RecPortfolio.Generate(size, seed))
        {
            RecChange change = new(
                [.. commit.Asserted.Select(q => (Of(q.Subject), Of(q.Predicate), Of(q.Object), Of(q.Graph)))],
                [.. commit.Retracted.Select(q => (Of(q.Subject), Of(q.Predicate), Of(q.Object), Of(q.Graph)))]);
            if (n++ < loaded)
            {
                await dataset.Dataset.CommitAsync(change.Request([]));
            }
            else
            {
                requests.Add(change);
            }
        }

        CommitRequestBuilder shapes = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Triples(composed))
        {
            shapes.Assert(s, p, o, ShapesGraphIri);
        }

        await dataset.Dataset.CommitAsync(shapes);
        await dataset.Dataset.MaintainAsync();
        Console.WriteLine("// REC " + size + ": " + loaded.ToString("N0", CultureInfo.InvariantCulture) + " commits and the shapes loaded in " + clock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s");
        return (dataset, requests);
    }

    /// <summary><c>--shapes-gate-affected &lt;S|M&gt;</c>.</summary>
    internal static async Task AffectedAsync(string sizeName)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        (FileDataset dataset, List<RecChange> held) = await LoadAsync(size, 20261015, ShapesGateBenchmarks.Held + 1);
        Counting counting = new(Gate(), Binding());
        await dataset.Dataset.CommitAsync(held[0].Request([counting]));
        counting.Reset();

        int refused = 0;
        foreach (RecChange change in held.Skip(1))
        {
            CommitResult result = await dataset.Dataset.CommitAsync(change.Request([counting]));
            refused += result.Outcome == CommitOutcome.Rejected ? 1 : 0;
        }

        double gate = counting.Gate.TotalMilliseconds;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"REC {size}: {counting.Commits} commits, {refused} refused; in the gate {gate / counting.Commits:F2} ms per commit; affected focus nodes {counting.Affected / (double)counting.Commits:F1} per commit (max {counting.MostAffected}); {gate * 1000 / Math.Max(1, counting.Affected):F0} µs per affected focus node"));
        await dataset.DisposeAsync();
    }

    private static string Root()
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Varve.slnx")))
        {
            d = d.Parent;
        }

        return d?.FullName ?? throw new InvalidOperationException("The repository root is not above " + AppContext.BaseDirectory + ".");
    }

    /// <summary>The gate, timed, and beside it, untimed, the focus nodes each commit affects.</summary>
    private sealed class Counting(ShapesGate inner, ShapesBinding binding) : ICommitValidator
    {
        private ShapesGraph? _shapes;

        internal TimeSpan Gate { get; private set; }

        internal long Affected { get; private set; }

        internal int MostAffected { get; private set; }

        internal int Commits { get; private set; }

        internal void Reset()
        {
            Gate = TimeSpan.Zero;
            Affected = 0;
            MostAffected = 0;
            Commits = 0;
        }

        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            long started = Stopwatch.GetTimestamp();
            ValidationVerdict verdict = inner.Validate(proposed, delta);
            Gate += Stopwatch.GetElapsedTime(started);
            Commits++;

            _shapes ??= Load();
            IncrementalOutcome outcome = IncrementalValidation.Compare(_shapes, GraphScopedQuadSource.Wrap(proposed, binding.Validates), GraphPattern.Any, delta, Options());
            int affected = outcome.Comparison?.AffectedFocusNodes.Count ?? 0;
            Affected += affected;
            MostAffected = Math.Max(MostAffected, affected);
            return verdict;
        }

        private static ShaclOptions Options() => new() { Extensions = [Varve.Shacl.Sparql.SparqlConstraints.Extension()], SubClassOfInShapesGraph = true };

        private static ShapesGraph Load()
        {
            InMemoryDatasetBuilder builder = new();
            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Triples())
            {
                builder.Add(s, p, o);
            }

            return ShapesGraph.Load(builder.ToDataset(), GraphPattern.DefaultGraph, Options()).Shapes
                ?? throw new InvalidOperationException("REC's shapes did not load.");
        }
    }
}

/// <summary>One commit of the portfolio's history, as terms.</summary>
internal sealed record RecChange((RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G)[] Asserted, (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G)[] Retracted)
{
    internal CommitRequestBuilder Request(IReadOnlyList<ICommitValidator> validators)
    {
        CommitRequestBuilder request = new() { Validators = validators };
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm g) in Asserted)
        {
            request.Assert(s, p, o, g);
        }

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm g) in Retracted)
        {
            request.Retract(s, p, o, g);
        }

        return request;
    }
}
