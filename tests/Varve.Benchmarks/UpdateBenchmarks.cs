// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Store;
using Varve.Store;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Update;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// The milestone 5c workloads as text, so that every engine — and pyoxigraph,
/// through <c>oxigraph/update.py</c> — reads the same bytes.
/// </summary>
/// <remarks>
/// The store is <see cref="StoreDataset"/>'s million quads. The update moves
/// every quad of one predicate in the named graphs to another predicate and
/// the reverse update moves them back, so a run can alternate the two over one
/// loaded store and every invocation does the same work. The graph to
/// canonicalise is <see cref="CanonGraph"/>.
/// </remarks>
internal static class UpdateWorkload
{
    internal const string Prefix = "PREFIX p: <http://example.org/store/predicate/>\n";

    /// <summary>Moves predicate 3 to predicate <c>moved</c> in every named graph.</summary>
    internal const string Move = Prefix + "DELETE { GRAPH ?g { ?s p:3 ?o } } INSERT { GRAPH ?g { ?s p:moved ?o } } WHERE { GRAPH ?g { ?s p:3 ?o } }";

    /// <summary>The reverse of <see cref="Move"/>.</summary>
    internal const string Back = Prefix + "DELETE { GRAPH ?g { ?s p:moved ?o } } INSERT { GRAPH ?g { ?s p:3 ?o } } WHERE { GRAPH ?g { ?s p:moved ?o } }";

    /// <summary>The quads <see cref="Move"/> moves: predicate 3, not in the default graph.</summary>
    internal static int Moved()
    {
        int count = 0;

        for (int i = 0; i < StoreDataset.Quads; i++)
        {
            if (i % 17 == 3 && i % 6 != 5)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary><c>INSERT DATA</c> of the first <paramref name="count"/> quads of <see cref="StoreDataset"/>.</summary>
    internal static string InsertData(int count)
    {
        (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] quads = StoreDataset.Build(count);
        StringBuilder text = new("INSERT DATA {\n");

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g) in quads)
        {
            string triple = Term(s) + " " + Term(p) + " " + Term(o) + " .";
            _ = g is null ? text.Append(triple).Append('\n') : text.Append("GRAPH ").Append(Term(g)).Append(" { ").Append(triple).Append(" }\n");
        }

        return text.Append('}').ToString();
    }

    /// <summary>The whole store as N-Quads, for the engines that load a file.</summary>
    internal static byte[] StoreNQuads(int count)
    {
        ArrayBufferWriter<byte> output = new();

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g) in StoreDataset.Build(count))
        {
            Write(output, s, p, o, g);
        }

        return output.WrittenSpan.ToArray();
    }

    internal static void Write(ArrayBufferWriter<byte> output, RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g)
    {
        string line = Term(s) + " " + Term(p) + " " + Term(o) + (g is null ? string.Empty : " " + Term(g)) + " .\n";
        output.Write(Encoding.UTF8.GetBytes(line));
    }

    internal static string Term(RdfTerm term)
    {
        Span<byte> buffer = stackalloc byte[512];

        if (!Varve.Turtle.NQuadsWriter.TryWriteTerm(term, buffer, out int written, default))
        {
            throw new InvalidOperationException("A benchmark term did not fit.");
        }

        return Encoding.UTF8.GetString(buffer[..written]);
    }

    /// <summary>
    /// Writes the files <c>oxigraph/update.py</c> reads: the two insert
    /// requests, the store, the move and its reverse, the graphs to
    /// canonicalise, and Varve's canonical form of each, which the script
    /// compares with Oxigraph's.
    /// </summary>
    internal static void Export(string directory)
    {
        Directory.CreateDirectory(directory);

        foreach (int count in UpdateBenchmarks.Sizes)
        {
            File.WriteAllText(Path.Combine(directory, "insert-" + count.ToString(CultureInfo.InvariantCulture) + ".ru"), InsertData(count));
        }

        File.WriteAllBytes(Path.Combine(directory, "store.nq"), StoreNQuads(StoreDataset.Quads));
        File.WriteAllText(Path.Combine(directory, "move.ru"), Move);
        File.WriteAllText(Path.Combine(directory, "back.ru"), Back);
        foreach (CanonShape shape in Enum.GetValues<CanonShape>())
        {
            string name = "canon-" + shape.ToString().ToLowerInvariant();
            File.WriteAllBytes(Path.Combine(directory, name + ".nt"), CanonGraph.NTriples(shape));
            File.WriteAllBytes(Path.Combine(directory, name + ".varve.nq"), RdfCanonicaliser.Canonicalise(CanonGraph.Build(shape)).NQuads.ToArray());
        }

        Console.WriteLine($"Wrote the update and canonicalisation workloads to {directory}: {Moved():N0} quads moved per update.");
    }
}

/// <summary>
/// <c>INSERT DATA</c> of N quads into an empty store: parsing the request,
/// evaluating it and committing it.
/// </summary>
/// <remarks>
/// Parsing is inside the measurement for every engine, because it is for
/// pyoxigraph, whose <c>update</c> takes text. Each invocation starts from an
/// empty store, made in the iteration setup and not timed.
/// </remarks>
[MemoryDiagnoser]
[WarmupCount(2)]
[IterationCount(8)]
[InvocationCount(1, 1)]
public class UpdateBenchmarks : IDisposable
{
    internal static readonly int[] Sizes = [10_000, 100_000];

    private string _text = string.Empty;
    private StoreDatasetType? _dataset;
    private TripleStore? _triples;

    [ParamsSource(nameof(Counts))]
    public int Count { get; set; }

    public static IEnumerable<int> Counts() => Sizes;

    [GlobalSetup]
    public void Setup() => _text = UpdateWorkload.InsertData(Count);

    [IterationSetup(Target = nameof(Varve))]
    public void EmptyVarve() => _dataset = StoreDatasetType.OpenAsync(new MemoryStorage(), new DatasetOptions { Clock = TimeProvider.System }).AsTask().GetAwaiter().GetResult();

    [IterationSetup(Target = nameof(DotNetRdf))]
    public void EmptyDotNetRdf() => _triples = new TripleStore();

    [IterationCleanup(Targets = [nameof(Varve), nameof(DotNetRdf)])]
    public void Check()
    {
        long quads = _dataset is not null ? CountQuads(_dataset) : Quads(_triples!);

        if (quads != Count)
        {
            throw new InvalidOperationException($"INSERT DATA of {Count} left {quads} quads.");
        }

        _dataset?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _dataset = null;
        _triples?.Dispose();
        _triples = null;
    }

    [Benchmark]
    public async Task<CommitOutcome> Varve()
    {
        Varve.Sparql.Algebra.Update update = SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(_text));
        return (await SparqlUpdate.ExecuteAsync(_dataset!, update, new UpdateOptions())).Outcome;
    }

    /// <summary>Varve's parse alone, to say how much of the row above is parsing.</summary>
    [Benchmark]
    public int VarveParseOnly() => SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(_text)).Operations.Count;

    [Benchmark(Baseline = true)]
    public int DotNetRdf()
    {
        SparqlUpdateCommandSet commands = new SparqlUpdateParser().ParseFromString(_text);
        new LeviathanUpdateProcessor(_triples!).ProcessCommandSet(commands);
        return commands.CommandCount;
    }

    internal static long CountQuads(StoreDatasetType dataset)
    {
        using DatasetView view = dataset.Pin();
        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        long count = 0;

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    internal static long Quads(TripleStore triples)
    {
        long count = 0;

        foreach (IGraph graph in triples.Graphs)
        {
            count += graph.Triples.Count;
        }

        return count;
    }

    public void Dispose()
    {
        _triples?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// <c>DELETE/INSERT … WHERE</c> over the million-quad store: 49,020 quads
/// moved from one predicate to another in every named graph, then moved back.
/// </summary>
/// <remarks>
/// Two invocations an iteration, the move and its reverse, so every invocation
/// reads the store the previous one left and does the same work; the time is
/// per request. The store is loaded once — 100 commits of 10,000 — and grows a
/// commit per request, which is what a store under updates does.
/// </remarks>
[MemoryDiagnoser]
[WarmupCount(2)]
[IterationCount(8)]
[InvocationCount(2, 1)]
public class DeleteInsertWhereBenchmarks : IDisposable
{
    private readonly Varve.Sparql.Algebra.Update[] _varve = new Varve.Sparql.Algebra.Update[2];
    private readonly SparqlUpdateCommandSet[] _dotNetRdf = new SparqlUpdateCommandSet[2];
    private StoreDatasetType? _dataset;
    private TripleStore? _triples;
    private int _turn;
    private int _moved;

    [GlobalSetup(Target = nameof(Varve))]
    public void LoadVarve()
    {
        _dataset = StoreDataset.LoadAsync(StoreDataset.Build(StoreDataset.Quads), 10_000).GetAwaiter().GetResult();
        _varve[0] = SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(UpdateWorkload.Move));
        _varve[1] = SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(UpdateWorkload.Back));
        _moved = UpdateWorkload.Moved();
    }

    [GlobalSetup(Target = nameof(DotNetRdf))]
    public void LoadDotNetRdf()
    {
        _triples = new TripleStore();
        new NQuadsParser().Load(_triples, new StreamReader(new MemoryStream(UpdateWorkload.StoreNQuads(StoreDataset.Quads)), Encoding.UTF8));
        _dotNetRdf[0] = new SparqlUpdateParser().ParseFromString(UpdateWorkload.Move);
        _dotNetRdf[1] = new SparqlUpdateParser().ParseFromString(UpdateWorkload.Back);
        _moved = UpdateWorkload.Moved();
        long quads = UpdateBenchmarks.Quads(_triples);

        if (quads != StoreDataset.Quads)
        {
            throw new InvalidOperationException($"dotNetRDF loaded {quads} quads, not {StoreDataset.Quads}.");
        }
    }

    [GlobalCleanup]
    public void Release()
    {
        // After an even number of requests the quads are back where they
        // started, and every one of them moved each time.
        long moved = _dataset is not null ? Count(_dataset) : _triples!.Graphs.Where(g => g.Name is not null).Sum(g => g.Triples.Count(t => t.Predicate is IUriNode { } p && p.ToString() == "http://example.org/store/predicate/3"));
        _dataset?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        if ((_turn & 1) == 0 && moved != _moved)
        {
            throw new InvalidOperationException($"{moved} quads of predicate 3 in the named graphs, not {_moved}.");
        }
    }

    private static long Count(StoreDatasetType dataset)
    {
        using DatasetView view = dataset.Pin();
        view.TryInternalise(RdfTerm.Iri("http://example.org/store/predicate/3"u8), out TermHandle predicate);
        using IQuadCursor cursor = view.Match(TermHandle.None, predicate, TermHandle.None, GraphPattern.AnyNamed);
        long count = 0;

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public async Task<CommitOutcome> Varve()
    {
        CommitResult result = await SparqlUpdate.ExecuteAsync(_dataset!, _varve[_turn++ & 1], new UpdateOptions());

        if (result.Outcome != CommitOutcome.Committed)
        {
            throw new InvalidOperationException($"The update was {result.Outcome}, not a commit.");
        }

        return result.Outcome;
    }

    [Benchmark(Baseline = true)]
    public int DotNetRdf()
    {
        new LeviathanUpdateProcessor(_triples!).ProcessCommandSet(_dotNetRdf[_turn++ & 1]);
        return _turn;
    }

    public void Dispose()
    {
        _triples?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// The graph the canonicalisation benchmark reads: 100,000 triples in the
/// default graph, from a fixed rule so that it reproduces without a file.
/// </summary>
/// <remarks>
/// 12,500 blank-node subjects with eight triples each: a type, a label and a
/// number that make most of them distinguishable at first degree, and five
/// links to other blank nodes by a stride that makes the links a single
/// connected structure rather than a forest. Three shapes:
/// <see cref="CanonShape.Blank"/>, every blank node told apart at first
/// degree; <see cref="CanonShape.Twins"/>, one subject in 50 sharing its label
/// and number with the one before it, so those pairs need the N-degree step;
/// and <see cref="CanonShape.Blank1000"/>, the same triples with only the first
/// 1,000 subjects blank and the rest IRIs, because dotNetRDF 3.5.2 refuses a
/// dataset of more than 1,000 blank nodes. Synthetic, and read as such: a real dataset's blank nodes are mostly
/// leaves.
/// </remarks>
internal static class CanonGraph
{
    internal const int Subjects = 12_500;

    internal static InMemoryDataset Build(CanonShape shape)
    {
        InMemoryDatasetBuilder builder = new();

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Triples(shape))
        {
            builder.Add(s, p, o);
        }

        return builder.ToDataset();
    }

    internal static byte[] NTriples(CanonShape shape)
    {
        ArrayBufferWriter<byte> output = new();

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in Triples(shape))
        {
            UpdateWorkload.Write(output, s, p, o, null);
        }

        return output.WrittenSpan.ToArray();
    }

    private static IEnumerable<(RdfTerm S, RdfTerm P, RdfTerm O)> Triples(CanonShape shape)
    {
        RdfTerm type = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);
        RdfTerm @class = RdfTerm.Iri("http://example.org/canon/Node"u8);
        RdfTerm label = RdfTerm.Iri("http://example.org/canon/label"u8);
        RdfTerm number = RdfTerm.Iri("http://example.org/canon/number"u8);
        RdfTerm link = RdfTerm.Iri("http://example.org/canon/link"u8);
        RdfTerm integer = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8);
        int[] strides = [1, 7, 97, 1_009, 4_999];

        for (int i = 0; i < Subjects; i++)
        {
            RdfTerm subject = Node(i, shape);
            int key = shape == CanonShape.Twins && i % 50 == 49 ? i - 1 : i;
            yield return (subject, type, @class);
            yield return (subject, label, RdfTerm.Literal(Encoding.UTF8.GetBytes("node " + key.ToString(CultureInfo.InvariantCulture))));
            yield return (subject, number, RdfTerm.Literal(Encoding.UTF8.GetBytes(key.ToString(CultureInfo.InvariantCulture)), integer));

            foreach (int stride in strides)
            {
                yield return (subject, link, Node((i + stride) % Subjects, shape));
            }
        }
    }

    private static RdfTerm Node(int i, CanonShape shape) => shape == CanonShape.Blank1000 && i >= 1_000
        ? RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/canon/n" + i.ToString(CultureInfo.InvariantCulture)))
        : RdfTerm.BlankNode(Encoding.UTF8.GetBytes("b" + i.ToString(CultureInfo.InvariantCulture)));
}

/// <summary>The shapes of <see cref="CanonGraph"/>.</summary>
public enum CanonShape
{
    /// <summary>12,500 blank nodes, each told apart at first degree.</summary>
    Blank,

    /// <summary>12,500 blank nodes, 250 pairs of which need the N-degree step.</summary>
    Twins,

    /// <summary>1,000 blank nodes among 12,500 subjects, the rest IRIs.</summary>
    Blank1000,
}

/// <summary>RDFC-1.0 over <see cref="CanonGraph"/>, SHA-256, to canonical N-Quads.</summary>
/// <remarks>
/// Both engines produce the canonical N-Quads document, and the setup checks
/// that the two documents are the same bytes. dotNetRDF 3.5.2 refuses a
/// dataset of more than 1,000 blank nodes ("Recursion limit reached"); the
/// setup reports that and its row is then NA.
/// </remarks>
[MemoryDiagnoser]
public class CanonicaliseBenchmarks : IDisposable
{
    private InMemoryDataset _dataset = new InMemoryDatasetBuilder().ToDataset();
    private TripleStore _triples = new();
    private string? _refused;

    [Params(CanonShape.Blank, CanonShape.Twins, CanonShape.Blank1000)]
    public CanonShape Shape { get; set; }

    [GlobalSetup]
    public void Load()
    {
        _dataset = CanonGraph.Build(Shape);
        Graph graph = new();
        new NTriplesParser().Load(graph, new StreamReader(new MemoryStream(CanonGraph.NTriples(Shape)), Encoding.UTF8));
        _triples = new TripleStore();
        _triples.Add(graph);

        string varve = Encoding.UTF8.GetString(RdfCanonicaliser.Canonicalise(_dataset).NQuads.Span);

        try
        {
            string dotNetRdf = new RdfCanonicalizer("SHA256").Canonicalize(_triples).SerializedNQuads;
            Console.WriteLine($"// {Shape}: {_dataset.Count.Value:N0} triples; Varve {varve.Length:N0} characters of canonical N-Quads, dotNetRDF {dotNetRdf.Length:N0}; same: {string.Equals(varve, dotNetRdf, StringComparison.Ordinal)}");
        }
#pragma warning disable CA1031 // ADR 0027: a baseline's refusal is reported with the numbers, not handled.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _refused = error.Message;
            Console.WriteLine($"// {Shape}: {_dataset.Count.Value:N0} triples; Varve {varve.Length:N0} characters of canonical N-Quads; dotNetRDF refused: {error.Message}");
        }
    }

    [Benchmark]
    public int Varve() => RdfCanonicaliser.Canonicalise(_dataset).NQuads.Length;

    [Benchmark(Baseline = true)]
    public int DotNetRdf() => _refused is null
        ? new RdfCanonicalizer("SHA256").Canonicalize(_triples).SerializedNQuads.Length
        : throw new InvalidOperationException("dotNetRDF refused this graph: " + _refused);

    public void Dispose()
    {
        _triples.Dispose();
        GC.SuppressFinalize(this);
    }
}
