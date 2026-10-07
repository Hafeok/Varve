// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Protocol.Http;

/// <summary>
/// Terms and quads as canonical N-Triples and N-Quads text (ADR 0061), the
/// form a graph result, a service description and every change line take.
/// N-Triples is also Turtle and TriG, which is why one line writer serves all
/// four syntaxes for terms that come from outside a store.
/// </summary>
internal static class TermLines
{
    private static readonly WriteOptions Canonical = new() { Syntax = RdfSyntax.NQuads, Canonical = true };

    /// <summary>One term, as N-Triples writes it.</summary>
    internal static void WriteTerm(IBufferWriter<byte> output, RdfTerm term)
    {
        int hint = 256;

        while (true)
        {
            Span<byte> span = output.GetSpan(hint);

            if (NQuadsWriter.TryWriteTerm(term, span, out int written, in Canonical))
            {
                output.Advance(written);
                return;
            }

            hint = Math.Max(hint, span.Length) * 2;
        }
    }

    /// <summary><c>s p o .</c> and a line feed.</summary>
    internal static void WriteTriple(IBufferWriter<byte> output, RdfTerm subject, RdfTerm predicate, RdfTerm @object)
    {
        WriteTerm(output, subject);
        Write(output, " "u8);
        WriteTerm(output, predicate);
        Write(output, " "u8);
        WriteTerm(output, @object);
        Write(output, " .\n"u8);
    }

    /// <summary>Raw bytes.</summary>
    internal static void Write(IBufferWriter<byte> output, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(output.GetSpan(bytes.Length));
        output.Advance(bytes.Length);
    }
}
