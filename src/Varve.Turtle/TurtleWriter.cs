using System;
using System.Buffers;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>
/// Writes Turtle and TriG, one statement at a time.
/// </summary>
/// <remarks>
/// <para>
/// A class rather than a static, because a Turtle document has state a single
/// statement cannot carry: the prefixes in scope and, in TriG, the graph block
/// that is currently open. <c>NQuadsWriter</c> stays static because N-Quads has
/// neither.
/// </para>
/// <para>
/// <strong>Streaming, and that decides the output's shape.</strong> Each quad
/// is written as it arrives, so the writer never holds a dataset and never
/// reorders one. In TriG that means a block is opened when the graph changes
/// and closed when it changes again: a caller whose quads are grouped by graph
/// gets one block per graph, and a caller whose quads are interleaved gets
/// several. Both are valid TriG and denote the same dataset. The alternative —
/// one block per graph always — needs the whole dataset in memory before the
/// first byte, which is the property this writer exists to keep.
/// </para>
/// <para>
/// <strong>No pretty-printing</strong> (<c>turtle.md</c> §7): no collection
/// syntax, no nested blank node property lists, no predicate or object lists,
/// and no abbreviation of a term that has a longer spelling. In particular
/// <c>rdf:type</c> is not written <c>a</c> and <c>"1"^^xsd:integer</c> is not
/// written <c>1</c>. Both are legal and both would make the writer's output
/// depend on a table of special cases; the one abbreviation it does make —
/// prefix compaction — is the one the caller asked for by declaring a prefix.
/// </para>
/// <para>
/// Nothing is allocated per quad. The prefix table is built once from the
/// declarations, and each statement is formatted straight into a span taken
/// from the output.
/// </para>
/// </remarks>
public sealed partial class TurtleWriter : IDisposable
{
    private readonly IBufferWriter<byte> _output;
    private readonly TurtleWriteOptions _options;
    private readonly PrefixTable _prefixes = new();
    private byte[] _openGraph = [];
    private bool _graphIsOpen;
    private bool _disposed;

    /// <summary>Creates a writer over <paramref name="output"/>.</summary>
    public TurtleWriter(IBufferWriter<byte> output, in TurtleWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _options = options;
    }

    /// <summary>
    /// Declares a prefix, writing the directive and using it from here on.
    /// </summary>
    /// <remarks>
    /// The writer never invents a prefix (<c>turtle.md</c> §7). A prefix nobody
    /// declared is one the reader of the output has to guess the meaning of,
    /// and guessing it from the IRIs is what this leaves to the caller, who
    /// knows what the document is about.
    /// </remarks>
    /// <param name="prefix">The name, without its colon. May be empty.</param>
    /// <param name="iri">The IRI it expands to.</param>
    public void DeclarePrefix(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> iri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!prefix.IsEmpty && !TurtleChars.IsPrefixName(prefix))
        {
            throw new ArgumentException(
                "Not a well-formed PNAME_NS. A writer that emitted this would produce a document its "
                + "own reader rejects.",
                nameof(prefix));
        }

        CloseGraph();
        _prefixes.Bind(prefix, iri);
        WriteDirective("@prefix "u8, prefix, hasName: true, iri);
    }

    /// <summary>
    /// Writes a <c>@base</c> directive.
    /// </summary>
    /// <remarks>
    /// <strong>It does not shorten anything.</strong> Every IRI this writer
    /// emits is absolute, so the directive changes nothing about the output and
    /// exists so that a caller reproducing a document can reproduce its
    /// directives too. Relativising against a base is a lossy transformation —
    /// several references resolve to one IRI — and a writer that did it would
    /// be choosing which one the author meant.
    /// </remarks>
    public void DeclareBase(ReadOnlySpan<byte> iri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CloseGraph();
        WriteDirective("@base "u8, default, hasName: false, iri);
    }

    /// <summary>Writes one quad.</summary>
    public void Write(in QuadView quad)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_options.Syntax == RdfSyntax.TriG)
        {
            // Not a ternary: QuadView.Graph throws for a quad in the default
            // graph, and both arms of a ternary are evaluated as arguments.
            if (quad.HasGraph)
            {
                OpenGraph(quad.Graph.Lexical, quad.Graph.Kind);
            }
            else
            {
                CloseGraph();
            }
        }

        int size = 256;

        while (true)
        {
            Span<byte> span = _output.GetSpan(size);
            SpanWriter writer = new(span);
            WriteStatement(ref writer, in quad);

            if (!writer.Overflowed)
            {
                _output.Advance(writer.Written);
                return;
            }

            size = Math.Max(size * 2, writer.Written);
        }
    }

    /// <summary>
    /// Writes one quad whose terms live in a quad source, materialising each
    /// term as it goes. The allocating path, for a caller holding handles.
    /// </summary>
    public void Write(in Quad quad, IQuadSource source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);

        RdfTerm subject = Externalise(source, quad.Subject);
        RdfTerm predicate = Externalise(source, quad.Predicate);
        RdfTerm obj = Externalise(source, quad.Object);
        RdfTerm? graph = quad.IsDefaultGraph ? null : Externalise(source, quad.Graph);

        if (_options.Syntax == RdfSyntax.TriG)
        {
            if (graph is null)
            {
                CloseGraph();
            }
            else
            {
                OpenGraph(graph.Lexical, graph.Kind);
            }
        }

        int size = 256;

        while (true)
        {
            Span<byte> span = _output.GetSpan(size);
            SpanWriter writer = new(span);

            WriteTerm(ref writer, subject);
            writer.Byte((byte)' ');
            WriteTerm(ref writer, predicate);
            writer.Byte((byte)' ');
            WriteTerm(ref writer, obj);
            writer.Bytes(" .\n"u8);

            if (!writer.Overflowed)
            {
                _output.Advance(writer.Written);
                return;
            }

            size = Math.Max(size * 2, writer.Written);
        }
    }

    /// <summary>
    /// Closes any open graph block, so that what has been written is a complete
    /// document.
    /// </summary>
    /// <remarks>
    /// Writing after this is allowed and reopens a block. Nothing is buffered,
    /// so there is nothing else to flush — the name says what a caller means by
    /// it, which is "make the output valid as it stands".
    /// </remarks>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CloseGraph();
    }

    /// <summary>Closes any open graph block. Idempotent.</summary>
    /// <remarks>
    /// The reason this type is disposable: a TriG document that ends inside a
    /// block is not a document, and leaving one open is the failure a caller is
    /// least likely to notice, because every byte written so far looks right.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CloseGraph();
        _disposed = true;
    }

    private static RdfTerm Externalise(IQuadSource source, TermHandle handle) =>
        source.TryExternalise(handle, out RdfTerm? term)
            ? term
            : throw new InvalidOperationException(
                "The quad source cannot externalise a term of this quad. A term whose key has been "
                + "destroyed has no serialisation, and writing a placeholder would claim it does.");

    private void WriteDirective(
        ReadOnlySpan<byte> keyword, ReadOnlySpan<byte> name, bool hasName, ReadOnlySpan<byte> iri)
    {
        int size = 256;

        while (true)
        {
            Span<byte> span = _output.GetSpan(size);
            SpanWriter writer = new(span);

            writer.Bytes(keyword);

            if (hasName)
            {
                writer.Bytes(name);
                writer.Bytes(": "u8);
            }

            writer.Byte((byte)'<');
            Escapes.WriteIri(ref writer, iri, _options.Canonical);
            writer.Bytes("> .\n"u8);

            if (!writer.Overflowed)
            {
                _output.Advance(writer.Written);
                return;
            }

            size = Math.Max(size * 2, writer.Written);
        }
    }

    /// <summary>
    /// Makes <paramref name="label"/> the open block, closing another first if
    /// one is open for a different graph.
    /// </summary>
    private void OpenGraph(ReadOnlySpan<byte> label, RdfTermKind kind)
    {
        if (_graphIsOpen && label.SequenceEqual(_openGraph))
        {
            return;
        }

        CloseGraph();
        _openGraph = label.ToArray();
        _graphIsOpen = true;

        int size = 256;

        while (true)
        {
            Span<byte> span = _output.GetSpan(size);
            SpanWriter writer = new(span);

            if (kind == RdfTermKind.BlankNode)
            {
                writer.Bytes("_:"u8);
                writer.Bytes(label);
            }
            else
            {
                WriteIriOrPrefixed(ref writer, label);
            }

            writer.Bytes(" {\n"u8);

            if (!writer.Overflowed)
            {
                _output.Advance(writer.Written);
                return;
            }

            size = Math.Max(size * 2, writer.Written);
        }
    }

    private void CloseGraph()
    {
        if (!_graphIsOpen)
        {
            return;
        }

        _graphIsOpen = false;
        _openGraph = [];

        Span<byte> span = _output.GetSpan(2);
        span[0] = (byte)'}';
        span[1] = (byte)'\n';
        _output.Advance(2);
    }

    private void WriteStatement(ref SpanWriter writer, in QuadView quad)
    {
        if (_graphIsOpen)
        {
            writer.Bytes("  "u8);
        }

        WriteTerm(ref writer, quad.Subject);
        writer.Byte((byte)' ');
        WriteTerm(ref writer, quad.Predicate);
        writer.Byte((byte)' ');
        WriteTerm(ref writer, quad.Object);
        writer.Bytes(" .\n"u8);
    }
}
