// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;
using Varve.Sparql.Algebra;
using Varve.Sparql.Writing;

namespace Varve.Sparql;

/// <summary>
/// Writes a query or an update request as text that parses back to the
/// identical tree (<c>docs/spec/sparql-algebra.md</c> §6).
/// </summary>
/// <remarks>
/// The text is canonical and not the author's: every IRI in full, every join
/// operand in its own braces, every binary expression parenthesised, the
/// projection always explicit. It says what the tree means and nothing about
/// how it was written.
/// </remarks>
public static class SparqlWriter
{
    /// <summary>Writes a query as UTF-8.</summary>
    public static void Write(Query query, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(output);
        Writer writer = new(output);
        writer.Write(query);
        writer.Complete();
    }

    /// <summary>Writes an update request as UTF-8.</summary>
    public static void Write(Update update, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(output);
        Writer writer = new(output);
        writer.Write(update);
        writer.Complete();
    }

    /// <summary>A query as a string.</summary>
    public static string ToText(Query query)
    {
        ArrayBufferWriter<byte> buffer = new();
        Write(query, buffer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>An update request as a string.</summary>
    public static string ToText(Update update)
    {
        ArrayBufferWriter<byte> buffer = new();
        Write(update, buffer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
