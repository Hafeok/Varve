// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Sparql.Results;

/// <summary>
/// Whether a literal can be written as a bare Turtle number or boolean: its
/// datatype is <c>xsd:integer</c>, <c>xsd:decimal</c>, <c>xsd:double</c> or
/// <c>xsd:boolean</c>, and its lexical form matches Turtle's <c>INTEGER</c>,
/// <c>DECIMAL</c>, <c>DOUBLE</c> or <c>BooleanLiteral</c> exactly — so that a
/// reader gives back the same term, datatype and lexical form both.
/// </summary>
internal static class TurtleNumber
{
    private static ReadOnlySpan<byte> Integer => "http://www.w3.org/2001/XMLSchema#integer"u8;

    private static ReadOnlySpan<byte> Decimal => "http://www.w3.org/2001/XMLSchema#decimal"u8;

    private static ReadOnlySpan<byte> Double => "http://www.w3.org/2001/XMLSchema#double"u8;

    private static ReadOnlySpan<byte> Boolean => "http://www.w3.org/2001/XMLSchema#boolean"u8;

    internal static bool IsBare(ReadOnlySpan<byte> lexical, ReadOnlySpan<byte> datatype)
    {
        if (datatype.SequenceEqual(Integer))
        {
            return IsInteger(lexical);
        }

        if (datatype.SequenceEqual(Decimal))
        {
            return IsDecimal(lexical);
        }

        if (datatype.SequenceEqual(Double))
        {
            return IsDouble(lexical);
        }

        return datatype.SequenceEqual(Boolean) && (lexical.SequenceEqual("true"u8) || lexical.SequenceEqual("false"u8));
    }

    // [19] INTEGER ::= [+-]? [0-9]+
    private static bool IsInteger(ReadOnlySpan<byte> s)
    {
        s = Sign(s);
        return s.Length > 0 && Digits(s) == s.Length;
    }

    // [20] DECIMAL ::= [+-]? [0-9]* '.' [0-9]+
    private static bool IsDecimal(ReadOnlySpan<byte> s)
    {
        s = Sign(s);
        s = s[Digits(s)..];
        return s.Length > 1 && s[0] == (byte)'.' && Digits(s[1..]) == s.Length - 1;
    }

    // [21] DOUBLE ::= [+-]? ([0-9]+ '.' [0-9]* EXPONENT | '.' [0-9]+ EXPONENT | [0-9]+ EXPONENT)
    private static bool IsDouble(ReadOnlySpan<byte> s)
    {
        s = Sign(s);
        int before = Digits(s);
        s = s[before..];
        int after = 0;

        if (s.Length > 0 && s[0] == (byte)'.')
        {
            after = Digits(s[1..]);
            s = s[(1 + after)..];
        }

        if (before == 0 && after == 0)
        {
            return false;
        }

        // [154s] EXPONENT ::= [eE] [+-]? [0-9]+
        if (s.Length < 2 || (s[0] != (byte)'e' && s[0] != (byte)'E'))
        {
            return false;
        }

        s = Sign(s[1..]);
        return s.Length > 0 && Digits(s) == s.Length;
    }

    private static ReadOnlySpan<byte> Sign(ReadOnlySpan<byte> s) =>
        s.Length > 0 && (s[0] == (byte)'+' || s[0] == (byte)'-') ? s[1..] : s;

    private static int Digits(ReadOnlySpan<byte> s)
    {
        int n = 0;

        while (n < s.Length && s[n] >= (byte)'0' && s[n] <= (byte)'9')
        {
            n++;
        }

        return n;
    }
}
