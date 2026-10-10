// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.JsonLd.Tests;

/// <summary>Materialises a JSON-LD toRdf as N-Quads lines, and an expansion as text, so a test states a whole document's output in strings.</summary>
internal static class Harness
{
    internal sealed record Read(JsonLdResult Result, List<string> Lines);

    internal static byte[] U(string text) => Encoding.UTF8.GetBytes(text);

    internal static string S(ReadOnlySpan<byte> utf8) => Encoding.UTF8.GetString(utf8);

    internal static Read ToRdf(string document, string? baseIri = "http://example.org/doc", RdfDirection direction = RdfDirection.Native, JsonLdDocumentLoader? loader = null)
    {
        List<string> lines = [];
        JsonLdOptions options = new()
        {
            BaseIri = baseIri is null ? default : U(baseIri),
            RdfDirection = direction,
            DocumentLoader = loader,
        };

        JsonLdResult result = JsonLdParser.Parse(U(document), (in QuadView quad) => lines.Add(Line(in quad)), in options);
        return new Read(result, lines);
    }

    internal static string Expand(string document, string? baseIri = "http://example.org/doc", JsonLdDocumentLoader? loader = null, bool indent = false)
    {
        ArrayBufferWriter output = new();
        JsonLdOptions options = new()
        {
            BaseIri = baseIri is null ? default : U(baseIri),
            DocumentLoader = loader,
            Indent = indent,
        };

        JsonLdResult result = JsonLdExpander.Expand(U(document), output, in options);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error!.ToString());
        }

        return S(output.Written);
    }

    internal static string FromRdf(string nquads, JsonLdWriteOptions options)
    {
        ArrayBufferWriter output = new();

        using (JsonLdWriter writer = new(output, in options))
        {
            ParseResult parsed = NQuadsParser.Parse(U(nquads), (in QuadView quad) => writer.Write(in quad), new ParseOptions { Syntax = RdfSyntax.NQuads });

            if (!parsed.Succeeded)
            {
                throw new InvalidOperationException(parsed.FirstError.ToString());
            }
        }

        return S(output.Written);
    }

    /// <summary>One quad as an N-Quads line without its final dot.</summary>
    internal static string Line(in QuadView quad)
    {
        string line = Term(quad.Subject) + " " + Term(quad.Predicate) + " " + Term(quad.Object);
        return quad.HasGraph ? line + " " + Term(quad.Graph) : line;
    }

    internal static string Term(in RdfTermView view)
    {
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
