// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Text;
using System.Globalization;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>The four classes of term id (ADR 0012), carried in the top two bits.</summary>
internal enum IdClass
{
    Canonical = 0,
    Blank = 1,
    Private = 2,
    Inline = 3,
}

/// <summary>
/// The in-memory id layout. Provisional by ADR 0045: the durable layout is
/// milestone 6's.
/// </summary>
/// <remarks>
/// Canonical and blank ids are counters from 1 within their class, so no id is
/// 0 and 0 remains the default graph. The private class is reserved and
/// nothing allocates it. An inline id carries a datatype tag in bits 61–56 and
/// a 56-bit payload.
/// </remarks>
internal static class TermIds
{
    private const int ClassShift = 62;
    private const ulong CounterMask = (1UL << ClassShift) - 1;
    private const int InlineTagShift = 56;
    private const ulong InlinePayloadMask = (1UL << InlineTagShift) - 1;

    internal const ulong InlineInteger = 1;
    internal const ulong InlineBoolean = 2;

    private const long InlineIntegerMax = (1L << 55) - 1;
    private const long InlineIntegerMin = -(1L << 55);

    internal static ReadOnlySpan<byte> XsdInteger => "http://www.w3.org/2001/XMLSchema#integer"u8;

    internal static ReadOnlySpan<byte> XsdBoolean => "http://www.w3.org/2001/XMLSchema#boolean"u8;

    [HotPath]
    internal static IdClass ClassOf(ulong id) => (IdClass)(id >> ClassShift);

    [HotPath]
    internal static long Counter(ulong id) => (long)(id & CounterMask);

    internal static ulong Canonical(long counter) => (ulong)counter;

    internal static ulong Blank(long counter) => ((ulong)IdClass.Blank << ClassShift) | (ulong)counter;

    /// <summary>
    /// The inline id for a literal, when it has one: canonical <c>xsd:integer</c>
    /// in range, or <c>xsd:boolean</c>. A literal takes an inline id only when
    /// its lexical form is canonical (ADR 0012's amendment), so that two terms
    /// can never become one.
    /// </summary>
    internal static bool TryInline(RdfTerm term, out ulong id)
    {
        id = 0;

        if (term.Kind != RdfTermKind.Literal || term.Datatype is null)
        {
            return false;
        }

        ReadOnlySpan<byte> datatype = term.DatatypeIri;
        ReadOnlySpan<byte> lexical = term.Lexical;

        if (datatype.SequenceEqual(XsdBoolean))
        {
            if (lexical.SequenceEqual("true"u8))
            {
                id = Inline(InlineBoolean, 1);
                return true;
            }

            if (lexical.SequenceEqual("false"u8))
            {
                id = Inline(InlineBoolean, 0);
                return true;
            }

            return false;
        }

        if (!datatype.SequenceEqual(XsdInteger) || !IsCanonicalInteger(lexical))
        {
            return false;
        }

        if (!Utf8Parser.TryParse(lexical, out long value, out int consumed) || consumed != lexical.Length)
        {
            return false;
        }

        if (value < InlineIntegerMin || value > InlineIntegerMax)
        {
            return false;
        }

        id = Inline(InlineInteger, (ulong)value & InlinePayloadMask);
        return true;
    }

    /// <summary>The literal an inline id stands for.</summary>
    internal static RdfTerm InlineTerm(ulong id)
    {
        ulong tag = (id >> InlineTagShift) & 0x3F;
        ulong payload = id & InlinePayloadMask;

        if (tag == InlineBoolean)
        {
            return RdfTerm.Literal(payload == 0 ? "false"u8 : "true"u8, RdfTerm.Iri(XsdBoolean));
        }

        // Sign-extend the 56-bit payload.
        long value = (long)(payload << 8) >> 8;
        return RdfTerm.Literal(
            System.Text.Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture)),
            RdfTerm.Iri(XsdInteger));
    }

    internal static bool IsValidInline(ulong id)
    {
        ulong tag = (id >> InlineTagShift) & 0x3F;
        return ClassOf(id) == IdClass.Inline
            && (tag == InlineInteger || (tag == InlineBoolean && (id & InlinePayloadMask) <= 1));
    }

    private static ulong Inline(ulong tag, ulong payload) =>
        ((ulong)IdClass.Inline << ClassShift) | (tag << InlineTagShift) | payload;

    /// <summary><c>-?(0|[1-9][0-9]*)</c>, without <c>-0</c>: the canonical xsd:integer lexical form.</summary>
    private static bool IsCanonicalInteger(ReadOnlySpan<byte> lexical)
    {
        int start = lexical.Length > 0 && lexical[0] == (byte)'-' ? 1 : 0;
        ReadOnlySpan<byte> digits = lexical[start..];

        if (digits.IsEmpty || digits.Length > 17)
        {
            return false;
        }

        if (digits[0] == (byte)'0')
        {
            return digits.Length == 1 && start == 0;
        }

        foreach (byte b in digits)
        {
            if (b < (byte)'0' || b > (byte)'9')
            {
                return false;
            }
        }

        return true;
    }
}
