// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.RdfXml;

/// <summary>
/// The character classes of XML names (XML 1.0 fifth edition §2.3, Namespaces
/// in XML 1.0 §3), over UTF-8 and over UTF-16, and the one question RDF/XML
/// asks of an IRI: where its local name begins.
/// </summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal static class XmlNames
{
    /// <summary><c>NameStartChar</c> less <c>:</c>: what may begin an <c>NCName</c>.</summary>
    internal static bool IsNcNameStart(int c) =>
        c is (>= 'A' and <= 'Z') or '_' or (>= 'a' and <= 'z')
            or (>= 0xC0 and <= 0xD6) or (>= 0xD8 and <= 0xF6) or (>= 0xF8 and <= 0x2FF)
            or (>= 0x370 and <= 0x37D) or (>= 0x37F and <= 0x1FFF) or (>= 0x200C and <= 0x200D)
            or (>= 0x2070 and <= 0x218F) or (>= 0x2C00 and <= 0x2FEF) or (>= 0x3001 and <= 0xD7FF)
            or (>= 0xF900 and <= 0xFDCF) or (>= 0xFDF0 and <= 0xFFFD) or (>= 0x10000 and <= 0xEFFFF);

    /// <summary><c>NameChar</c> less <c>:</c>: what may continue an <c>NCName</c>.</summary>
    internal static bool IsNcNameChar(int c) =>
        IsNcNameStart(c) || c is '-' or '.' or (>= '0' and <= '9') or 0xB7
            or (>= 0x0300 and <= 0x036F) or (>= 0x203F and <= 0x2040);

    /// <summary>Whether <paramref name="utf8"/> is an <c>NCName</c>: what <c>rdf:ID</c> and <c>rdf:nodeID</c> require.</summary>
    internal static bool IsNcName(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return false;
        }

        bool first = true;

        while (!utf8.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(utf8, out Rune rune, out int consumed) != OperationStatus.Done)
            {
                return false;
            }

            if (first ? !IsNcNameStart(rune.Value) : !IsNcNameChar(rune.Value))
            {
                return false;
            }

            first = false;
            utf8 = utf8[consumed..];
        }

        return true;
    }

    /// <summary>
    /// Splits an IRI into the namespace name and the local name RDF/XML
    /// writes it as (RDF 1.1 XML Syntax §2.7, informally: "the longest
    /// suffix that is an NCName"). False when no suffix is an <c>NCName</c> —
    /// a predicate ending in a digit, a slash or a fragment sign — which is a
    /// predicate the syntax cannot spell.
    /// </summary>
    internal static bool TrySplitQName(ReadOnlySpan<byte> iri, out int localStart)
    {
        // Walk backwards over NCName characters, remembering the last place a
        // start character stood: that is where the longest legal local name
        // begins. A ':' is not an NCName character, so "http:" never joins.
        localStart = -1;
        int at = iri.Length;

        while (at > 0)
        {
            if (Rune.DecodeLastFromUtf8(iri[..at], out Rune rune, out int consumed) != OperationStatus.Done
                || !IsNcNameChar(rune.Value))
            {
                break;
            }

            at -= consumed;

            if (IsNcNameStart(rune.Value))
            {
                localStart = at;
            }
        }

        return localStart > 0 && localStart < iri.Length;
    }
}
