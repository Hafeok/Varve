// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;

namespace Varve.Xsd;

/// <summary>
/// <c>xsd:string</c> (XML Schema 1.1 Part 2 §3.3.1): every string is its
/// own value, so this type has no value struct and one operation.
/// </summary>
/// <remarks>
/// SPARQL 1.1 §17.3 compares strings with <c>fn:compare</c> under the
/// codepoint collation, which orders by Unicode code point. Over valid UTF-8,
/// code-point order is byte order, so the byte form is a sequence comparison
/// and allocates nothing. The <c>char</c> form compares code points too,
/// which UTF-16 code unit order gets wrong above the Basic Multilingual
/// Plane, where a surrogate pair sorts below the private-use block.
/// </remarks>
public static class XsdString
{
    /// <summary>Code-point order over two UTF-8 strings.</summary>
    public static int CompareCodePoints(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        Math.Sign(left.SequenceCompareTo(right));

    /// <summary>Code-point order over two UTF-16 strings.</summary>
    public static int CompareCodePoints(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        int i = 0;
        int j = 0;

        while (i < left.Length && j < right.Length)
        {
            Rune.DecodeFromUtf16(left[i..], out Rune a, out int consumedA);
            Rune.DecodeFromUtf16(right[j..], out Rune b, out int consumedB);

            if (a.Value != b.Value)
            {
                return a.Value < b.Value ? -1 : 1;
            }

            i += consumedA;
            j += consumedB;
        }

        return (left.Length - i).CompareTo(right.Length - j) switch
        {
            0 => 0,
            < 0 => -1,
            _ => 1,
        };
    }
}
