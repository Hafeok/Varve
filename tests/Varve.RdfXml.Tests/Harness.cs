// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.RdfXml.Tests;

/// <summary>Materialises an RDF/XML parse as N-Triples lines, so a test states a whole document's output in strings.</summary>
internal static class Harness
{
    internal sealed record Read(RdfXmlParseResult Result, List<string> Lines, List<(string Prefix, string Iri)> Namespaces);

    internal static byte[] U(string text) => Encoding.UTF8.GetBytes(text);

    internal static string S(ReadOnlySpan<byte> utf8) => Encoding.UTF8.GetString(utf8);

    internal static Read Parse(string document, string? baseIri = "http://example.org/doc", bool validateIris = true)
    {
        List<string> lines = [];
        List<(string, string)> namespaces = [];
        RdfXmlOptions options = new()
        {
            BaseIri = baseIri is null ? default : U(baseIri),
            ValidateIris = validateIris,
            OnNamespace = (prefix, iri) => namespaces.Add((S(prefix), S(iri))),
        };

        RdfXmlParseResult result = RdfXmlParser.Parse(U(document), (in QuadView quad) => lines.Add(Line(in quad)), in options);
        return new Read(result, lines, namespaces);
    }

    /// <summary>One quad as a canonical N-Quads line without its final dot.</summary>
    internal static string Line(in QuadView quad)
    {
        string line = Term(quad.Subject) + " " + Term(quad.Predicate) + " " + Term(quad.Object);
        return quad.HasGraph ? line + " " + Term(quad.Graph) : line;
    }

    internal static string Term(in RdfTermView view)
    {
        // Not the canonical form: it lowercases a language tag, and the
        // fixed-point property needs the tag as the reader held it.
        RdfTerm term = view.Materialise();
        WriteOptions options = new() { Syntax = RdfSyntax.NQuads, Canonical = false };
        byte[] buffer = new byte[256];

        while (!NQuadsWriter.TryWriteTerm(term, buffer, out _, in options))
        {
            buffer = new byte[buffer.Length * 2];
        }

        NQuadsWriter.TryWriteTerm(term, buffer, out int written, in options);
        return Encoding.UTF8.GetString(buffer, 0, written);
    }

    /// <summary>Writes the quads of an N-Quads document as RDF/XML, through <see cref="RdfXmlWriter"/>.</summary>
    internal static string WriteXml(string nquads, Action<RdfXmlWriter>? declare = null, bool indent = true)
    {
        ArrayBufferWriter output = new();
        RdfXmlWriteOptions options = new() { Indent = indent };

        using (RdfXmlWriter writer = new(output, in options))
        {
            declare?.Invoke(writer);
            ParseResult parsed = NQuadsParser.Parse(U(nquads), (in QuadView quad) => writer.Write(in quad), new ParseOptions { Syntax = RdfSyntax.NQuads });

            if (!parsed.Succeeded)
            {
                throw new InvalidOperationException(parsed.FirstError.ToString());
            }
        }

        return Encoding.UTF8.GetString(output.Written);
    }

    /// <summary>A growable sink, the shape every Varve writer takes.</summary>
    internal sealed class ArrayBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[1024];
        private int _written;

        public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _written);

        public void Reset() => _written = 0;

        public void Advance(int count) => _written += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsSpan(_written);
        }

        private void Ensure(int sizeHint)
        {
            int needed = Math.Max(sizeHint, 1);

            if (_buffer.Length - _written < needed)
            {
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _written + needed));
            }
        }
    }
}
