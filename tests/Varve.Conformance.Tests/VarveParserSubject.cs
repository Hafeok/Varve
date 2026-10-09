// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Varve.Rdf;
using Varve.JsonLd;
using Varve.RdfXml;
using Varve.Turtle;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>Varve.Turtle</c> as the harness needs to see it.
/// </summary>
/// <remarks>
/// <para>
/// The adapter wraps the real API rather than the real API being shaped to
/// suit the harness, which is what <see cref="IParserSubject"/> says it is for.
/// A syntax test is one small document, accepted or rejected, so the whole file
/// goes in at once; <see cref="ParseSplit"/> is the same parse with the bytes
/// handed over in two pieces.
/// </para>
/// <para>
/// Quads are collected even for a syntax test. They cost one materialisation
/// per quad on files of a few lines, they are what an evaluation test compares,
/// and they mean a failure report can say what was parsed rather than only that
/// something was.
/// </para>
/// </remarks>
internal sealed class VarveParserSubject : IParserSubject
{
    [ModuleInitializer]
    internal static void Register() => ParserSubjects.Current = new VarveParserSubject();

    public ParseOutcome Parse(RdfFormat format, string path, string baseIri)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Collector collector = new();

        if (IsRdfXml(format))
        {
            RdfXmlParseResult xml = RdfXmlParser.Parse(bytes, collector.XmlHandler, RdfXmlOptionsFor(baseIri));
            return xml.Succeeded ? ParseOutcome.Parsed(collector.Quads) : ParseOutcome.Rejected(xml.Error!.ToString());
        }

        if (format == RdfFormat.JsonLd)
        {
            JsonLdResult jsonLd = JsonLdParser.Parse(bytes, collector.JsonLdHandler, JsonLdOptionsFor(baseIri, null));
            return jsonLd.Succeeded ? ParseOutcome.Parsed(collector.Quads) : ParseOutcome.Rejected(jsonLd.Error!.ToString());
        }

        ParseResult result = IsLineBased(format)
            ? NQuadsParser.Parse(bytes, collector.Handler, LineOptions(format))
            : TurtleParser.Parse(bytes, collector.Handler, TurtleOptionsFor(format, baseIri));

        return Outcome(result, collector);
    }

    public ParseOutcome Parse(ManifestEntry entry)
    {
        if (entry.Format != RdfFormat.JsonLd)
        {
            return Parse(entry.Format, entry.ActionPath, entry.ActionIri);
        }

        byte[] bytes = File.ReadAllBytes(entry.ActionPath);
        Collector collector = new();
        JsonLdResult jsonLd = JsonLdParser.Parse(bytes, collector.JsonLdHandler, JsonLdOptionsFor(entry.ActionIri, entry.JsonLd));
        return jsonLd.Succeeded ? ParseOutcome.Parsed(collector.Quads) : ParseOutcome.Rejected(jsonLd.Error!.ToString());
    }

    public ParseOutcome ParseSplit(ManifestEntry entry, int at)
    {
        byte[] bytes = File.ReadAllBytes(entry.ActionPath);
        Collector collector = new();
        ReadOnlySequence<byte> sequence = Split(bytes, at);
        RdfFormat format = entry.Format;
        string baseIri = entry.ActionIri;

        if (IsRdfXml(format))
        {
            RdfXmlParseResult xml = RdfXmlParser.Parse(in sequence, collector.XmlHandler, RdfXmlOptionsFor(baseIri));
            return xml.Succeeded ? ParseOutcome.Parsed(collector.Quads) : ParseOutcome.Rejected(xml.Error!.ToString());
        }

        if (format == RdfFormat.JsonLd)
        {
            JsonLdResult jsonLd = JsonLdParser.Parse(in sequence, collector.JsonLdHandler, JsonLdOptionsFor(baseIri, entry.JsonLd));
            return jsonLd.Succeeded ? ParseOutcome.Parsed(collector.Quads) : ParseOutcome.Rejected(jsonLd.Error!.ToString());
        }

        ParseResult result = IsLineBased(format)
            ? NQuadsParser.Parse(in sequence, collector.Handler, LineOptions(format))
            : TurtleParser.Parse(in sequence, collector.Handler, TurtleOptionsFor(format, baseIri));

        return Outcome(result, collector);
    }

    /// <summary>
    /// The options a json-ld-api entry runs under: its stated base or the
    /// input's IRI, its expandContext, its rdfDirection or the
    /// specification's default of none, and the suite's document loader
    /// (ADR 0112; json-ld.md §1).
    /// </summary>
    internal static JsonLdOptions JsonLdOptionsFor(string baseIri, JsonLdCase? options) => new()
    {
        BaseIri = Encoding.UTF8.GetBytes(options?.BaseIri ?? baseIri),
        ExpandContext = options?.ExpandContextPath is null ? default : File.ReadAllBytes(options.ExpandContextPath),
        ExpandContextIri = options?.ExpandContextIri is null ? default : Encoding.UTF8.GetBytes(options.ExpandContextIri),
        RdfDirection = options?.Direction ?? RdfDirection.None,
        DocumentLoader = TestData.LoadJsonLdDocument,
    };

    private static bool IsLineBased(RdfFormat format) =>
        format is RdfFormat.NTriples or RdfFormat.NQuads or RdfFormat.NTriples12 or RdfFormat.NQuads12;

    internal static bool IsRdfXml(RdfFormat format) => format is RdfFormat.RdfXml or RdfFormat.RdfXml12;

    private static RdfXmlOptions RdfXmlOptionsFor(string baseIri) => new() { BaseIri = Encoding.UTF8.GetBytes(baseIri) };

    /// <summary>The edition a format is read as: the rdf12 suites under RDF 1.2, the rdf11 suites under RDF 1.1 (ADR 0110).</summary>
    internal static RdfVersion VersionOf(RdfFormat format) =>
        format is RdfFormat.NTriples12 or RdfFormat.NQuads12 or RdfFormat.Turtle12 or RdfFormat.TriG12 ? RdfVersion.Rdf12 : RdfVersion.Rdf11;

    private static ParseOptions LineOptions(RdfFormat format) => new()
    {
        Syntax = format is RdfFormat.NQuads or RdfFormat.NQuads12 ? RdfSyntax.NQuads : RdfSyntax.NTriples,
        Version = VersionOf(format),
    };

    /// <remarks>
    /// N-Triples and N-Quads take no base: they require absolute IRIs, so a
    /// relative one is a fault their suites test for and supplying a base would
    /// hide it.
    /// </remarks>
    private static TurtleOptions TurtleOptionsFor(RdfFormat format, string baseIri) => new()
    {
        Syntax = format is RdfFormat.TriG or RdfFormat.TriG12 ? RdfSyntax.TriG : RdfSyntax.Turtle,
        BaseIri = Encoding.UTF8.GetBytes(baseIri),
        Version = VersionOf(format),
    };

    private static ParseOutcome Outcome(ParseResult result, Collector collector) =>
        result.Succeeded
            ? ParseOutcome.Parsed(collector.Quads)
            : ParseOutcome.Rejected(result.FirstError.ToString());

    /// <summary>
    /// <paramref name="bytes"/> as two segments meeting at <paramref name="at"/>.
    /// </summary>
    /// <remarks>
    /// Always two segments, even when the split is at 0 or at the end: a
    /// sequence with one segment takes the span fast path, and the point here
    /// is to exercise the one that stitches.
    /// </remarks>
    private static ReadOnlySequence<byte> Split(byte[] bytes, int at)
    {
        Segment first = new(new ReadOnlyMemory<byte>(bytes, 0, at), 0);
        Segment second = first.Append(new ReadOnlyMemory<byte>(bytes, at, bytes.Length - at));
        return new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
    }

    private sealed class Collector
    {
        private readonly WriteOptions _writeOptions = new() { Syntax = RdfSyntax.NQuads };

        internal List<ParsedQuad> Quads { get; } = [];

        internal QuadHandler Handler => Collect;

        internal RdfXmlQuadHandler XmlHandler => Collect;

        internal JsonLdQuadHandler JsonLdHandler => Collect;

        private void Collect(in QuadView quad)
        {
            Quads.Add(new ParsedQuad(
                Term(quad.Subject),
                Term(quad.Predicate),
                Term(quad.Object),
                quad.HasGraph ? Term(quad.Graph) : null));
        }

        /// <summary>One term in canonical N-Triples syntax.</summary>
        private string Term(in RdfTermView view)
        {
            RdfTerm term = view.Materialise();
            byte[] buffer = new byte[256];

            while (!NQuadsWriter.TryWriteTerm(term, buffer, out _, in _writeOptions))
            {
                buffer = new byte[buffer.Length * 2];
            }

            NQuadsWriter.TryWriteTerm(term, buffer, out int written, in _writeOptions);
            return Encoding.UTF8.GetString(buffer, 0, written);
        }
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        internal Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        internal Segment Append(ReadOnlyMemory<byte> memory)
        {
            Segment next = new(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }
}
