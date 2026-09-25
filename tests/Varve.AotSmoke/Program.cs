// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.AotSmoke;

/// <summary>
/// Parses a file under Native AOT and prints what it found.
/// </summary>
/// <remarks>
/// <para>
/// Constraint 2 says Varve runs under Native AOT. A published binary that
/// nobody runs proves only that ILC did not refuse; this runs it and checks the
/// answer, because the interesting failures under AOT are at run time and
/// silent — a missing generic instantiation, a trimmed type, a static
/// constructor that never ran.
/// </para>
/// <para>
/// It writes and re-reads what it parsed as well, so the writer is covered
/// too, and it returns a non-zero exit code on any disagreement.
/// </para>
/// </remarks>
internal static class Program
{
    private const int Quads = 1_000;

    private static long observed;

    internal static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : WriteSampleFile();

        try
        {
            return Run(path);
        }
        catch (IOException error)
        {
            Console.Error.WriteLine("aot-smoke: " + error.Message);
            return 2;
        }
    }

    private static int Run(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ParseOptions options = new() { Syntax = RdfSyntax.NQuads };

        ParseResult parsed = NQuadsParser.Parse(bytes, Count, options);

        if (!parsed.Succeeded)
        {
            Console.Error.WriteLine("aot-smoke: parse failed at " + parsed.FirstError.ToString());
            return 1;
        }

        // Round trip: write what was parsed, parse that, and compare counts.
        BufferWriter output = new();
        NQuadsParser.Parse(bytes, (in QuadView quad) => NQuadsWriter.Write(output, in quad, new WriteOptions { Syntax = RdfSyntax.NQuads }), options);

        long before = observed;
        ParseResult reparsed = NQuadsParser.Parse(output.Written, Count, options);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"quads {parsed.QuadCount}, rewritten {output.Written.Length} bytes, reparsed {reparsed.QuadCount}"));

        if (parsed.QuadCount != reparsed.QuadCount || observed - before != before)
        {
            Console.Error.WriteLine("aot-smoke: the round trip disagreed with the first parse.");
            return 1;
        }

        // An IRI resolved through Varve.Iri, so layer 0 is exercised too and
        // not merely linked.
        Span<byte> resolved = stackalloc byte[64];

        if (!IriRefResolves(resolved, out string text))
        {
            Console.Error.WriteLine("aot-smoke: IRI resolution failed.");
            return 1;
        }

        Console.WriteLine("resolved " + text);

        return Turtle();
    }

    /// <summary>
    /// Reads Turtle and TriG and writes them back, under Native AOT.
    /// </summary>
    /// <remarks>
    /// The Turtle reader leans on things a trimmer can remove without a build
    /// error: a delegate invoked only through a field, a generic instantiated
    /// once, a <c>ref struct</c> whose members are reached indirectly. Parsing
    /// N-Quads does not exercise any of the constructs Turtle adds — the
    /// statement buffer, the prefix table, the blank node naming, the writer's
    /// state — so it would not notice their loss. This does.
    /// </remarks>
    private static int Turtle()
    {
        byte[] document = System.Text.Encoding.UTF8.GetBytes(
            "@prefix p: <http://example.org/> .\n"
            + "p:s p:p \"value \\u00E9\"@en , 1.5e3 ;\n"
            + "  p:q [ p:r ( <rel> p:t ) ] ;\n"
            + "  a p:C .\n");

        TurtleOptions read = new()
        {
            Syntax = RdfSyntax.Turtle,
            BaseIri = System.Text.Encoding.UTF8.GetBytes("http://example.org/base/"),
        };

        long before = observed;
        ParseResult parsed = TurtleParser.Parse(document, Count, in read);

        if (!parsed.Succeeded || parsed.QuadCount != 9)
        {
            Console.Error.WriteLine(
                "aot-smoke: turtle parse gave " + parsed.QuadCount.ToString(CultureInfo.InvariantCulture)
                + " quads, first error " + parsed.FirstError.ToString());
            return 1;
        }

        // Write it back through the Turtle writer, with a prefix declared so
        // that compaction runs, then read the result and compare.
        BufferWriter output = new();
        TurtleWriteOptions write = default;

        using (TurtleWriter writer = new(output, in write))
        {
            writer.DeclarePrefix("p"u8, "http://example.org/"u8);
            TurtleParser.Parse(document, (in QuadView quad) => writer.Write(in quad), in read);
        }

        ParseResult reparsed = TurtleParser.Parse(output.Written, Count, in read);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"turtle {parsed.QuadCount} quads, rewritten {output.Written.Length} bytes, "
            + $"reparsed {reparsed.QuadCount}"));

        if (parsed.QuadCount != reparsed.QuadCount || observed == before)
        {
            Console.Error.WriteLine("aot-smoke: the turtle round trip disagreed with the first parse.");
            return 1;
        }

        return TriG();
    }

    private static int TriG()
    {
        byte[] document = System.Text.Encoding.UTF8.GetBytes(
            "<http://example.org/g> { <http://example.org/s> <http://example.org/p> [] . }\n");

        TurtleOptions read = new() { Syntax = RdfSyntax.TriG };
        bool named = false;

        ParseResult parsed = TurtleParser.Parse(
            document,
            (in QuadView quad) => named = quad.HasGraph,
            in read);

        if (!parsed.Succeeded || parsed.QuadCount != 1 || !named)
        {
            Console.Error.WriteLine(
                "aot-smoke: trig parse gave " + parsed.QuadCount.ToString(CultureInfo.InvariantCulture)
                + " quads, graph " + named.ToString() + ", first error " + parsed.FirstError.ToString());
            return 1;
        }

        Console.WriteLine("trig 1 quad in a named graph");
        return Store().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Opens an in-memory store, commits, pins, checkpoints and reads as-of,
    /// under Native AOT.
    /// </summary>
    /// <remarks>
    /// The store leans on things a trimmer removes without a build error: an
    /// async state machine per call, a <c>MemoryManager</c> that reinterprets a
    /// checkpoint's bytes as keys, <c>SHA256</c> on every commit, and a
    /// generic sort over a struct key. The reopen at the end reads the log
    /// back, verifies its chain, and loads the checkpoint.
    /// </remarks>
    private static async ValueTask<int> Store()
    {
        Varve.Store.MemoryStorage storage = new();
        Varve.Store.DatasetOptions options = new() { Clock = TimeProvider.System };
        RdfTerm p = RdfTerm.Iri("http://example.org/p"u8);

        await using (Varve.Store.Dataset dataset = await Varve.Store.Dataset.OpenAsync(storage, options))
        {
            Varve.Store.CommitResult first = await dataset.CommitAsync(new Varve.Store.CommitRequest()
                .Assert(RdfTerm.Iri("http://example.org/a"u8), p, RdfTerm.Literal("1"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8)))
                .Assert(RdfTerm.BlankNode("x"u8), p, RdfTerm.Literal("chat"u8, "en"u8), RdfTerm.Iri("http://example.org/g"u8)));
            Varve.Store.CommitResult second = await dataset.CommitAsync(new Varve.Store.CommitRequest()
                .Retract(RdfTerm.Iri("http://example.org/a"u8), p, RdfTerm.Literal("1"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8)))
                .Assert(RdfTerm.Iri("http://example.org/b"u8), p, RdfTerm.TripleTerm(RdfTerm.Iri("http://example.org/a"u8), p, RdfTerm.Iri("http://example.org/c"u8))));

            await dataset.CheckpointAsync(1);

            using Varve.Store.DatasetView pinned = dataset.Pin();
            using Varve.Store.DatasetView asOf = await dataset.AsOfAsync(1);
            int now = CountQuads(pinned);
            int then = CountQuads(asOf);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"store: {first.Outcome}({first.Position}), {second.Outcome}({second.Position}), pinned {now} quads, as-of 1 {then} quads"));

            if (second.Position != 2 || now != 2 || then != 2)
            {
                Console.Error.WriteLine("aot-smoke: the store disagreed with itself.");
                return 1;
            }
        }

        await using Varve.Store.Dataset reopened = await Varve.Store.Dataset.OpenAsync(storage, options);

        if (reopened.Head != 2 || reopened.Checkpoints.Count != 1)
        {
            Console.Error.WriteLine("aot-smoke: the reopened store lost its log or its checkpoint.");
            return 1;
        }

        Console.WriteLine("store: reopened at 2 with a checkpoint at 1");
        return Sparql();
    }

    /// <summary>
    /// Parses a query and an update, prints the algebra through the serialiser,
    /// parses that back, and requires the identical tree, under Native AOT.
    /// </summary>
    /// <remarks>
    /// The parser leans on things ILC can drop or get wrong without a build
    /// error: records with synthesised equality over forty node types, a
    /// dictionary's alternate lookup by span, pooled arrays returned through a
    /// ref struct, UTF-16 to UTF-8 transcoding. The constructs reach a property
    /// path, a subquery, an aggregate, a reified triple with its 1.2 expansion,
    /// and an update with a WHERE clause.
    /// </remarks>
    private static int Sparql()
    {
        string query =
            "PREFIX ex: <http://example.org/>\n"
            + "SELECT ?s (COUNT(?o) AS ?n) WHERE {\n"
            + "  ?s ex:p/ex:q ?o . << ?s ex:r 1 >> ex:said ?w .\n"
            + "  OPTIONAL { ?s ex:t ?v FILTER(?v > 2) }\n"
            + "  { SELECT ?s WHERE { ?s a ex:C } LIMIT 5 }\n"
            + "} GROUP BY ?s HAVING (COUNT(?o) > 1) ORDER BY DESC(?n)";

        Varve.Sparql.Algebra.Query parsed = Varve.Sparql.SparqlParser.ParseQuery(query.AsSpan());
        string written = Varve.Sparql.SparqlWriter.ToText(parsed);
        Varve.Sparql.Algebra.Query again = Varve.Sparql.SparqlParser.ParseQuery(System.Text.Encoding.UTF8.GetBytes(written));

        Console.WriteLine(written);

        if (!parsed.Equals(again))
        {
            Console.Error.WriteLine("aot-smoke: the serialised query parses to a different tree.");
            return 1;
        }

        Varve.Sparql.Algebra.Update update = Varve.Sparql.SparqlParser.ParseUpdate(
            "PREFIX ex: <http://example.org/> DELETE { ?s ex:p ?o } INSERT { GRAPH ex:g { ?s ex:q ?o } } WHERE { ?s ex:p ?o }"u8);
        Varve.Sparql.Algebra.Update updateAgain = Varve.Sparql.SparqlParser.ParseUpdate(
            System.Text.Encoding.UTF8.GetBytes(Varve.Sparql.SparqlWriter.ToText(update)));

        if (!update.Equals(updateAgain) || update.Operations.Count != 1)
        {
            Console.Error.WriteLine("aot-smoke: the serialised update parses to a different tree.");
            return 1;
        }

        if (Varve.Sparql.SparqlParser.TryParseQuery("SELECT * WHERE { ?s ?p }"u8, default, out _, out Varve.Sparql.SparqlParseError error))
        {
            Console.Error.WriteLine("aot-smoke: an ill-formed query parsed.");
            return 1;
        }

        Console.WriteLine("sparql: query and update round-tripped through the algebra; error reported at " + error.ToString());
        return Evaluation().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Loads Turtle into the store and evaluates a basic graph pattern with a
    /// filter, an aggregate, a property path closure and the hash functions
    /// over the pinned view, under Native AOT.
    /// </summary>
    /// <remarks>
    /// The evaluator leans on things ILC can drop without a build error:
    /// iterator state machines per operator, generic hash sets keyed by row,
    /// the inline array of the function arguments, <c>Regex</c> and the hash
    /// primitives reached only through a switch. MD5 is the package's own (RFC
    /// 1321), since the browser has none; the others are the platform's.
    /// </remarks>
    private static async ValueTask<int> Evaluation()
    {
        (string Bgp, string Aggregate, string Path, string Hashes) answer = await Smoke.EvaluateAsync();
        Console.WriteLine("evaluation: " + answer.Bgp + "; " + answer.Aggregate + "; " + answer.Path);
        Console.WriteLine("evaluation: " + answer.Hashes);

        if (!string.Equals(answer.Bgp, Smoke.ExpectedBgp, StringComparison.Ordinal)
            || !string.Equals(answer.Aggregate, Smoke.ExpectedAggregate, StringComparison.Ordinal)
            || !string.Equals(answer.Path, Smoke.ExpectedPath, StringComparison.Ordinal)
            || !string.Equals(answer.Hashes, Smoke.ExpectedHashes, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("aot-smoke: the evaluator's answers are not the expected ones.");
            return 1;
        }

        return Update().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Milestone 5c under Native AOT: an update committed against the pinned
    /// head, a query's results written as JSON, and RDFC-1.0 under SHA-256
    /// and SHA-384. The writer and the canonicaliser use IncrementalHash and
    /// pooled buffers, which ILC could trim without a build error.
    /// </summary>
    private static async ValueTask<int> Update()
    {
        (string update, string json, string canonical, string sha384) = await Smoke.UpdateAsync();
        Console.WriteLine("update: " + update + "; " + json.Length + " bytes of results JSON; canonical form of " + canonical.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length + " quads");

        if (!string.Equals(update, Smoke.ExpectedUpdate, StringComparison.Ordinal)
            || !string.Equals(json, Smoke.ExpectedJson, StringComparison.Ordinal)
            || !string.Equals(canonical, Smoke.ExpectedCanonical, StringComparison.Ordinal)
            || !string.Equals(sha384, Smoke.ExpectedCanonical384, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("aot-smoke: update, results or canonicalisation are not the expected ones:\n" + update + "\n" + json + canonical + "---\n" + sha384);
            return 1;
        }

        return 0;
    }

    private static int CountQuads(Varve.Store.DatasetView source)
    {
        int count = 0;

        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    private static bool IriRefResolves(Span<byte> destination, out string text)
    {
        bool ok = Varve.Iri.IriRef.TryResolve(
            "http://example.org/a/b"u8, "../c/d"u8, destination, out int written);

        text = ok ? System.Text.Encoding.UTF8.GetString(destination[..written]) : "";
        return ok && text == "http://example.org/c/d";
    }

    private static readonly QuadHandler Count = static (in QuadView quad) =>
        observed += quad.Subject.Lexical.Length + quad.Object.Lexical.Length;

    private static string WriteSampleFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "varve-aot-smoke.nq");
        using StreamWriter writer = new(path, append: false);

        for (int i = 0; i < Quads; i++)
        {
            writer.Write("<http://example.org/s/");
            writer.Write(i.ToString(CultureInfo.InvariantCulture));
            writer.Write("> <http://example.org/p> \"value \\u00E9\"@en <http://example.org/g> .\n");
        }

        return path;
    }

    /// <summary>A buffer writer, because the AOT app takes no dependencies of its own.</summary>
    private sealed class BufferWriter : System.Buffers.IBufferWriter<byte>
    {
        private byte[] _bytes = new byte[4096];
        private int _written;

        internal ReadOnlySpan<byte> Written => _bytes.AsSpan(0, _written);

        public void Advance(int count) => _written += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _bytes.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _bytes.AsSpan(_written);
        }

        private void Ensure(int sizeHint)
        {
            int wanted = _written + Math.Max(sizeHint, 1);

            if (_bytes.Length < wanted)
            {
                Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, wanted));
            }
        }
    }
}
