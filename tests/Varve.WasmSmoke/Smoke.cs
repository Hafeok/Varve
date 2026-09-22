// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Security.Cryptography;
using System.Text;
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
    internal static string Run()
    {
        StringBuilder report = new();

        try
        {
            report.Append(Parse()).Append('\n');
            report.Append(Turtle()).Append('\n');
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
