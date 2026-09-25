// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.WasmSmoke;

/// <summary>
/// The two things that have to be true in a browser, run in one.
/// </summary>
/// <remarks>
/// <para>
/// Constraint 3 says Varve runs in a browser. Publishing without a warning
/// shows only that the trimmer was satisfied; this runs and checks the answers.
/// </para>
/// <para>
/// The second half is <strong>ADR 0020's acceptance condition</strong>. That
/// decision chose AES-CBC with HMAC-SHA-256 partly because AES-GCM, AES-CCM
/// and ChaCha20Poly1305 are unsupported on browser WebAssembly, and it rested
/// that claim on a .NET release note. A release note is not a build. This runs
/// the exact composition the ADR specifies — HKDF derive, AES-CBC encrypt,
/// HMAC-SHA-256 over IV, ciphertext, key id and term id,
/// <see cref="CryptographicOperations.FixedTimeEquals"/>, then decrypt — and
/// also asserts that a tampered tag is rejected, because a MAC that verifies
/// everything is not a MAC.
/// </para>
/// </remarks>
// CA1416 is the claim under test, not a nuisance. The platform-compatibility
// analyzer says Aes.Create() is unsupported on browser; the .NET 7 release
// note says AES-CBC is supported on WebAssembly via SubtleCrypto with a
// managed fallback; and the cross-platform cryptography table has no browser
// column for symmetric encryption at all. Three official sources, two of them
// disagreeing. Suppressing the analyzer here is what lets the build run and
// answer the question — the answer is recorded in ADR 0020, and the exit code
// of the browser run is what decides it, not this pragma.
#pragma warning disable CA1416
internal static partial class Smoke
{
    private const int Quads = 200;

    [JSExport]
    internal static async Task<string> Run()
    {
        StringBuilder report = new();

        try
        {
            report.Append(Parse()).Append('\n');
            report.Append(Turtle()).Append('\n');
            report.Append(await Store()).Append('\n');
            report.Append(Sparql()).Append('\n');
            report.Append(await Evaluate()).Append('\n');
            report.Append(await Update()).Append('\n');
            report.Append(Crypto()).Append('\n');
            Expect();
            report.Append("OK");
        }
        catch (Exception error)
        {
            report.Append("FAIL ").Append(error.GetType().Name).Append(": ").Append(error.Message);
        }

        return report.ToString();
    }

    /// <summary>
    /// Reads Turtle and TriG in the browser, and writes Turtle back.
    /// </summary>
    /// <remarks>
    /// N-Quads exercises none of what Turtle adds — the statement buffer, the
    /// prefix table, the blank node naming, the writer's state — so a browser
    /// runtime that broke one of them would pass the N-Quads probe. The
    /// constructs here are chosen to reach each: a predicate-object list, an
    /// object list, a collection, a nested property list, an escape, and a TriG
    /// graph block.
    /// </remarks>
    private static string Turtle()
    {
        byte[] document = Encoding.UTF8.GetBytes(
            "@prefix p: <http://example.org/> .\n"
            + "p:s p:p \"value \\u00E9\"@en , 1.5e3 ;\n"
            + "  p:q [ p:r ( <rel> p:t ) ] ;\n"
            + "  a p:C .\n");

        TurtleOptions read = new()
        {
            Syntax = RdfSyntax.Turtle,
            BaseIri = Encoding.UTF8.GetBytes("http://example.org/base/"),
        };

        ParseResult parsed = TurtleParser.Parse(document, static (in QuadView _) => { }, in read);

        if (!parsed.Succeeded || parsed.QuadCount != 9)
        {
            throw new InvalidOperationException(
                "turtle: " + parsed.QuadCount.ToString(CultureInfo.InvariantCulture)
                + " quads, first error " + parsed.FirstError.ToString());
        }

        Writer output = new();
        TurtleWriteOptions write = default;

        using (TurtleWriter writer = new(output, in write))
        {
            writer.DeclarePrefix("p"u8, "http://example.org/"u8);
            TurtleParser.Parse(document, (in QuadView quad) => writer.Write(in quad), in read);
        }

        ParseResult reparsed = TurtleParser.Parse(output.Written, static (in QuadView _) => { }, in read);

        if (reparsed.QuadCount != parsed.QuadCount)
        {
            throw new InvalidOperationException(
                "turtle: round trip gave " + reparsed.QuadCount.ToString(CultureInfo.InvariantCulture)
                + " quads, expected " + parsed.QuadCount.ToString(CultureInfo.InvariantCulture));
        }

        TurtleOptions trig = new() { Syntax = RdfSyntax.TriG };
        bool named = false;

        ParseResult block = TurtleParser.Parse(
            Encoding.UTF8.GetBytes("<http://example.org/g> { <http://example.org/s> <http://example.org/p> [] . }"),
            (in QuadView quad) => named = quad.HasGraph,
            in trig);

        if (!block.Succeeded || block.QuadCount != 1 || !named)
        {
            throw new InvalidOperationException("trig: no quad in a named graph");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"turtle: {parsed.QuadCount} quads, rewritten {output.Written.Length} bytes, "
            + $"reparsed {reparsed.QuadCount}; trig: 1 quad in a named graph");
    }

    /// <summary>
    /// Opens an in-memory store in the browser, commits, pins, checkpoints,
    /// reads as-of, and reopens it from its own log.
    /// </summary>
    /// <remarks>
    /// Asynchronous all the way, as ADR 0018 requires of the storage contract:
    /// the browser has one thread, and a store that blocked on its own
    /// <c>ValueTask</c>s would deadlock here rather than anywhere else. SHA-256
    /// runs on every commit, which ADR 0014 rested on the browser supporting.
    /// </remarks>
    private static async Task<string> Store()
    {
        Varve.Store.MemoryStorage storage = new();
        Varve.Store.DatasetOptions options = new() { Clock = TimeProvider.System };
        RdfTerm p = RdfTerm.Iri("http://example.org/p"u8);
        RdfTerm one = RdfTerm.Literal("1"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8));
        int now;
        int then;

        await using (Varve.Store.Dataset dataset = await Varve.Store.Dataset.OpenAsync(storage, options))
        {
            await dataset.CommitAsync(new Varve.Store.CommitRequest()
                .Assert(RdfTerm.Iri("http://example.org/a"u8), p, one)
                .Assert(RdfTerm.BlankNode("x"u8), p, RdfTerm.Literal("chat"u8, "en"u8), RdfTerm.Iri("http://example.org/g"u8)));
            Varve.Store.CommitResult second = await dataset.CommitAsync(new Varve.Store.CommitRequest()
                .Retract(RdfTerm.Iri("http://example.org/a"u8), p, one)
                .Assert(RdfTerm.Iri("http://example.org/b"u8), p, RdfTerm.TripleTerm(RdfTerm.Iri("http://example.org/a"u8), p, one)));
            await dataset.CheckpointAsync(1);

            using Varve.Store.DatasetView pinned = dataset.Pin();
            using Varve.Store.DatasetView asOf = await dataset.AsOfAsync(1);
            now = Count(pinned);
            then = Count(asOf);

            if (second.Position != 2 || now != 2 || then != 2)
            {
                throw new InvalidOperationException(
                    "store: position " + second.Position.ToString(CultureInfo.InvariantCulture)
                    + ", pinned " + now.ToString(CultureInfo.InvariantCulture) + ", as-of " + then.ToString(CultureInfo.InvariantCulture));
            }
        }

        await using Varve.Store.Dataset reopened = await Varve.Store.Dataset.OpenAsync(storage, options);

        if (reopened.Head != 2 || reopened.Checkpoints.Count != 1)
        {
            throw new InvalidOperationException("store: the reopened store lost its log or its checkpoint");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"store: committed 2, pinned {now} quads, as-of 1 {then} quads, reopened at {reopened.Head} with a checkpoint");
    }

    /// <summary>
    /// Parses a query and an update in the browser, prints the algebra through
    /// the serialiser, parses that back, and requires the identical tree.
    /// </summary>
    /// <remarks>
    /// The parser leans on things a trimmer can remove or a browser runtime
    /// can get wrong: records with synthesised equality over forty node types,
    /// a dictionary's alternate lookup by span, pooled arrays, UTF-16 to UTF-8
    /// transcoding. The constructs here reach a property path, a subquery, an
    /// aggregate, a reified triple with its 1.2 expansion, and an update with a
    /// WHERE clause.
    /// </remarks>
    private static string Sparql()
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
        Varve.Sparql.Algebra.Query again = Varve.Sparql.SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(written));

        if (!parsed.Equals(again))
        {
            throw new InvalidOperationException("sparql: the serialised query parses to a different tree:\n" + written);
        }

        Varve.Sparql.Algebra.Update update = Varve.Sparql.SparqlParser.ParseUpdate(
            "PREFIX ex: <http://example.org/> DELETE { ?s ex:p ?o } INSERT { GRAPH ex:g { ?s ex:q ?o } } WHERE { ?s ex:p ?o }"u8);
        Varve.Sparql.Algebra.Update updateAgain = Varve.Sparql.SparqlParser.ParseUpdate(
            Encoding.UTF8.GetBytes(Varve.Sparql.SparqlWriter.ToText(update)));

        if (!update.Equals(updateAgain) || update.Operations.Count != 1)
        {
            throw new InvalidOperationException("sparql: the serialised update parses to a different tree");
        }

        Varve.Sparql.SparqlParseError error = default;

        if (Varve.Sparql.SparqlParser.TryParseQuery("SELECT * WHERE { ?s ?p }"u8, default, out _, out error))
        {
            throw new InvalidOperationException("sparql: an ill-formed query parsed");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"sparql: query round-tripped through {written.Length} characters of algebra, update round-tripped, error at {error.Line}:{error.Column}");
    }

    /// <summary>
    /// Loads Turtle into the store and evaluates a basic graph pattern with a
    /// filter, an aggregate, a property path closure and the five hash
    /// functions over the pinned view, in the browser (milestone 5b). The hash
    /// line is compared whole: SPARQL's <c>MD5()</c> is the package's own RFC
    /// 1321 implementation because the platform's is not here, and the others
    /// are the platform's, which the crypto probes pin.
    /// </summary>
    private static async Task<string> Evaluate()
    {
        (string bgp, string aggregate, string path, string hashes) = await EvaluateAsync();
        if (!string.Equals(bgp, ExpectedBgp, StringComparison.Ordinal)
            || !string.Equals(aggregate, ExpectedAggregate, StringComparison.Ordinal)
            || !string.Equals(path, ExpectedPath, StringComparison.Ordinal)
            || !string.Equals(hashes, ExpectedHashes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("evaluation: " + bgp + "; " + aggregate + "; " + path + "; " + hashes);
        }

        return "evaluation: " + bgp + "; " + aggregate + "; " + path + "; the five hashes of \"abc\" as FIPS 180 and RFC 1321 give them";
    }

    /// <summary>
    /// Milestone 5c: an update committed against the pinned head, a query's
    /// results written as SPARQL results JSON, and a small dataset
    /// canonicalised with RDFC-1.0 under SHA-256 and SHA-384, each compared
    /// whole with what the AOT host produces.
    /// </summary>
    private static async Task<string> Update()
    {
        (string update, string json, string canonical, string sha384) = await UpdateAsync();
        if (!string.Equals(update, ExpectedUpdate, StringComparison.Ordinal)
            || !string.Equals(json, ExpectedJson, StringComparison.Ordinal)
            || !string.Equals(canonical, ExpectedCanonical, StringComparison.Ordinal)
            || !string.Equals(sha384, ExpectedCanonical384, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("update: " + update + "\n" + json + canonical + "---\n" + sha384);
        }

        return "update: " + update + "; " + json.Length + " bytes of results JSON; RDFC-1.0 under SHA-256 and SHA-384 as the AOT host gives it";
    }

    private static int Count(Varve.Store.DatasetView source)
    {
        int count = 0;

        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    /// <summary>A minimal buffer writer, so the browser build needs no extra package.</summary>
    private sealed class Writer : System.Buffers.IBufferWriter<byte>
    {
        private byte[] _bytes = new byte[1024];
        private int _written;

        internal ReadOnlySpan<byte> Written => _bytes.AsSpan(0, _written);

        public void Advance(int count) => _written += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Grow(sizeHint);
            return _bytes.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Grow(sizeHint);
            return _bytes.AsSpan(_written);
        }

        private void Grow(int sizeHint)
        {
            int needed = _written + Math.Max(sizeHint, 1);

            if (needed > _bytes.Length)
            {
                Array.Resize(ref _bytes, Math.Max(needed, _bytes.Length * 2));
            }
        }
    }

    private static string Parse()
    {
        StringBuilder builder = new();

        for (int i = 0; i < Quads; i++)
        {
            builder.Append("<http://example.org/s/").Append(i)
                .Append("> <http://example.org/p> \"value \\u00E9\"@en <http://example.org/g> .\n");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
        ParseOptions options = new() { Syntax = RdfSyntax.NQuads };
        long lengths = 0;

        ParseResult result = NQuadsParser.Parse(
            bytes,
            (in QuadView quad) => lengths += quad.Subject.Lexical.Length + quad.Object.Language.Length,
            options);

        if (!result.Succeeded || result.QuadCount != Quads)
        {
            throw new InvalidOperationException(
                "parse: " + result.QuadCount.ToString(CultureInfo.InvariantCulture)
                + " quads, first error " + result.FirstError.ToString());
        }

        if (!Varve.Iri.IriRef.IsAbsolute("http://example.org/a"u8)
            || Varve.Iri.IriRef.IsAbsolute("/a"u8))
        {
            throw new InvalidOperationException("iri: absolute check disagreed");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"parse: {result.QuadCount} quads, {lengths} bytes of subject and language");
    }

    /// <summary>
    /// What the browser actually offers, probed one primitive at a time.
    /// </summary>
    /// <remarks>
    /// This was <strong>ADR 0020's acceptance condition</strong>, and it did not
    /// hold: on .NET 10 in a real browser <c>Aes.Create()</c> throws
    /// <see cref="PlatformNotSupportedException"/> and no symmetric cipher of
    /// any kind is available. It is now <strong>ADR 0028's first condition</strong>
    /// as well, from the other direction — 0028 builds its cipher out of
    /// SHA-256, HMAC-SHA-256, HKDF and a constant-time comparison precisely
    /// because those four are the ones that do run here.
    /// <para>
    /// So the probe records the capability rather than asserting the
    /// composition. Each line says what was observed, and the expectations
    /// below are pinned to what is true today: if a future runtime makes a
    /// symmetric cipher available in the browser, this fails and says so,
    /// which is when the successor to ADR 0020 can be decided.
    /// </para>
    /// </remarks>
    private static string Crypto()
    {
        StringBuilder report = new();

        report.Append("crypto: ");
        report.Append(Probe("RandomNumberGenerator", static () =>
        {
            byte[] bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return bytes.Length == 32;
        }));

        report.Append(Probe("SHA256", static () => SHA256.HashData("varve"u8).Length == 32));

        // SPARQL's SHA1(), SHA384(), SHA512() and MD5() are what these four
        // are probed for; the weak two are the functions' algorithms, not a
        // choice (ADR 0048).
#pragma warning disable CA5350, CA5351
        report.Append(Probe("SHA1", static () => SHA1.HashData("varve"u8).Length == 20));
        report.Append(Probe("SHA384", static () => SHA384.HashData("varve"u8).Length == 48));
        report.Append(Probe("SHA512", static () => SHA512.HashData("varve"u8).Length == 64));
        report.Append(Probe("IncrementalHash-SHA256", static () => Incremental(HashAlgorithmName.SHA256, 32)));
        report.Append(Probe("IncrementalHash-SHA384", static () => Incremental(HashAlgorithmName.SHA384, 48)));
        report.Append(Probe("MD5", static () => MD5.HashData("varve"u8).Length == 16));
#pragma warning restore CA5350, CA5351
        report.Append(Probe("HMACSHA256", static () => HMACSHA256.HashData(new byte[32], "varve"u8).Length == 32));

        report.Append(Probe("HKDF", static () =>
        {
            byte[] once = HKDF.DeriveKey(HashAlgorithmName.SHA256, new byte[32], 32, info: "varve/enc"u8.ToArray());
            byte[] again = HKDF.DeriveKey(HashAlgorithmName.SHA256, new byte[32], 32, info: "varve/enc"u8.ToArray());

            // Deterministic derivation is what makes ADR 0028's synthetic IV
            // portable, and its key separation possible.
            return once.Length == 32 && once.AsSpan().SequenceEqual(again);
        }));

        report.Append(Probe("FixedTimeEquals", static () =>
        {
            byte[] tag = HMACSHA256.HashData(new byte[32], "varve"u8);
            byte[] same = HMACSHA256.HashData(new byte[32], "varve"u8);
            byte[] other = HMACSHA256.HashData(new byte[32], "varvf"u8);

            // A comparison that says yes to everything is not a comparison, so
            // both directions are probed.
            return CryptographicOperations.FixedTimeEquals(tag, same)
                && !CryptographicOperations.FixedTimeEquals(tag, other);
        }));

        report.Append(Probe("AES-CBC", static () =>
        {
            using Aes aes = Aes.Create();
            aes.Key = new byte[32];
            byte[] ciphertext = aes.EncryptCbc("varve"u8, new byte[16], PaddingMode.PKCS7);
            return aes.DecryptCbc(ciphertext, new byte[16], PaddingMode.PKCS7).AsSpan().SequenceEqual("varve"u8);
        }));

        report.Append(Probe("AES-GCM", static () => AesGcm.IsSupported));
        report.Append(Probe("AES-CCM", static () => AesCcm.IsSupported));
        report.Append(Probe("ChaCha20Poly1305", static () => ChaCha20Poly1305.IsSupported));

        return report.ToString().TrimEnd(',', ' ');
    }

    /// <summary>
    /// Runs one probe and renders it as <c>name=yes</c>, <c>name=no</c>, or
    /// <c>name=threw(Type)</c>. A probe never propagates: the point is the
    /// whole picture, not the first thing that fails.
    /// </summary>
    /// <summary>
    /// Hashes in two appends and checks the digest against the one-shot's, which
    /// is how the canonicaliser uses it.
    /// </summary>
    private static bool Incremental(HashAlgorithmName algorithm, int length)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(algorithm);
        hash.AppendData("var"u8);
        hash.AppendData("ve"u8);
        byte[] digest = hash.GetHashAndReset();
        byte[] once = algorithm == HashAlgorithmName.SHA256 ? SHA256.HashData("varve"u8) : SHA384.HashData("varve"u8);
        return digest.Length == length && digest.AsSpan().SequenceEqual(once);
    }

    private static string Probe(string name, Func<bool> check)
    {
        string outcome;

        try
        {
            outcome = check() ? "yes" : "no";
        }
        catch (PlatformNotSupportedException)
        {
            outcome = "unsupported";
        }
        catch (CryptographicException)
        {
            outcome = "threw(CryptographicException)";
        }

        Observed[name] = outcome;
        return name + "=" + outcome + ", ";
    }

    internal static readonly System.Collections.Generic.SortedDictionary<string, string> Observed = new(StringComparer.Ordinal);

    /// <summary>
    /// What the browser offered on the day ADR 0020's condition was tested,
    /// pinned so that a change is reported rather than noticed by accident.
    /// </summary>
    /// <remarks>
    /// These are not aspirations. Every row is a fact about .NET 10 on
    /// browser-wasm that Varve has to live with. The last three say that
    /// <strong>no symmetric cipher of any kind is available in a browser</strong>,
    /// which is what failed ADR 0020; the first five are ADR 0028's first
    /// condition, and they hold. If a future runtime changes any of them this
    /// fails, and failing is the correct behaviour — a cipher appearing in the
    /// browser is the signal that 0028's alternatives are worth revisiting, and
    /// a primitive disappearing would break 0028 itself.
    /// </remarks>
    private static readonly (string Name, string Expected)[] Pinned =
    [
        ("RandomNumberGenerator", "yes"),
        ("SHA256", "yes"),
        ("HMACSHA256", "yes"),
        ("HKDF", "yes"),
        ("FixedTimeEquals", "yes"),

        // Milestone 5b: SPARQL's hash functions. MD5 is the one the browser
        // lacks, which is why Varve.Sparql.Evaluation carries its own (RFC 1321).
        ("SHA1", "yes"),
        ("SHA384", "yes"),
        ("SHA512", "yes"),

        // Milestone 5c: RDFC-1.0 hashes through IncrementalHash, SHA-256 by
        // default and SHA-384 when selected (ADR 0059).
        ("IncrementalHash-SHA256", "yes"),
        ("IncrementalHash-SHA384", "yes"),
        ("MD5", "threw(CryptographicException)"),

        ("AES-CBC", "unsupported"),
        ("AES-GCM", "no"),
        ("AES-CCM", "no"),
        ("ChaCha20Poly1305", "no"),
    ];

    private static void Expect()
    {
        foreach ((string name, string expected) in Pinned)
        {
            string actual = Observed.TryGetValue(name, out string? value) ? value : "(not probed)";

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "browser capability changed: " + name + " was pinned as '" + expected
                    + "' and is now '" + actual + "'. See ADR 0020.");
            }
        }
    }

    private static byte[] Mac(byte[] key, byte[] iv, byte[] ciphertext, ulong keyId, ulong termId)
    {
        byte[] message = new byte[iv.Length + ciphertext.Length + 16];
        iv.CopyTo(message, 0);
        ciphertext.CopyTo(message, iv.Length);
        BitConverter.TryWriteBytes(message.AsSpan(iv.Length + ciphertext.Length, 8), keyId);
        BitConverter.TryWriteBytes(message.AsSpan(iv.Length + ciphertext.Length + 8, 8), termId);

        return HMACSHA256.HashData(key, message);
    }

    internal static void Main()
    {
        // The JavaScript host calls Run through [JSExport]; nothing happens here.
    }
}
#pragma warning restore CA1416
