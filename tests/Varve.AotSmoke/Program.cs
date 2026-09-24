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
