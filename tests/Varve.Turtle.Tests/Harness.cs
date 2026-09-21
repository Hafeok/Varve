using System;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;

namespace Varve.Turtle.Tests;

/// <summary>
/// Materialises a parse, so that a test can state what it expects in terms of
/// owned terms rather than of views it cannot hold.
/// </summary>
internal static class Harness
{
    internal static byte[] U(string text) => Encoding.UTF8.GetBytes(text);

    internal static string S(ReadOnlySpan<byte> utf8) => Encoding.UTF8.GetString(utf8);

    internal sealed record Row(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object, RdfTerm? Graph);

    internal static (ParseResult Result, List<Row> Rows) Parse(string document, ParseOptions options = default)
    {
        List<Row> rows = [];
        ParseResult result = NQuadsParser.Parse(U(document), rows.Collect(), options);
        return (result, rows);
    }

    /// <summary>
    /// A handler that materialises each quad into <paramref name="rows"/>. The
    /// closure is the point: the streaming path must not need one, and a test
    /// that wants the quads afterwards must.
    /// </summary>
    internal static QuadHandler Collect(this List<Row> rows) => (in QuadView quad) =>
        rows.Add(new Row(
            quad.Subject.Materialise(),
            quad.Predicate.Materialise(),
            quad.Object.Materialise(),
            quad.HasGraph ? quad.Graph.Materialise() : null));

    internal static List<ParseError> Errors(this List<ParseError> sink, out ErrorHandler handler)
    {
        handler = (in ParseError error) =>
        {
            sink.Add(error);
            return ErrorAction.Continue;
        };

        return sink;
    }

    internal static string Write(string document, WriteOptions options = default)
    {
        ArrayBufferWriter writer = new();
        ParseOptions parseOptions = new()
        {
            Syntax = options.Syntax,
        };

        NQuadsParser.Parse(U(document), (in QuadView quad) => NQuadsWriter.Write(writer, in quad, options), parseOptions);
        return Encoding.UTF8.GetString(writer.Written);
    }

    /// <summary>
    /// A minimal <see cref="System.Buffers.IBufferWriter{T}"/>, so the tests do
    /// not depend on one the writer might be tempted to special-case.
    /// </summary>
    internal sealed class ArrayBufferWriter : System.Buffers.IBufferWriter<byte>
    {
        private byte[] _bytes = new byte[64];
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
