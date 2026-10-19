// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Varve.Xsd;

/// <summary>
/// A set of Unicode scalar values as sorted, disjoint, non-adjacent inclusive
/// ranges. Surrogate code points are never members: they are not characters,
/// and a character outside the BMP is one member, written as a surrogate pair
/// only when the set is emitted for .NET (ADR 0153).
/// </summary>
internal sealed class CodePointSet
{
    internal const int MaxCodePoint = 0x10FFFF;
    private const int SurrogateFirst = 0xD800;
    private const int SurrogateLast = 0xDFFF;

    private readonly List<(int First, int Last)> _ranges;

    private CodePointSet(List<(int First, int Last)> ranges) => _ranges = ranges;

    internal static CodePointSet Empty => new([]);

    internal static CodePointSet All => Range(0, MaxCodePoint);

    internal bool IsEmpty => _ranges.Count == 0;

    internal IReadOnlyList<(int First, int Last)> Ranges => _ranges;

    internal static CodePointSet Single(int codePoint) => Range(codePoint, codePoint);

    /// <summary>The range, without the surrogate block.</summary>
    internal static CodePointSet Range(int first, int last)
    {
        List<(int, int)> ranges = [];
        if (first <= last)
        {
            if (last < SurrogateFirst || first > SurrogateLast)
            {
                ranges.Add((first, last));
            }
            else
            {
                if (first < SurrogateFirst)
                {
                    ranges.Add((first, SurrogateFirst - 1));
                }

                if (last > SurrogateLast)
                {
                    ranges.Add((SurrogateLast + 1, last));
                }
            }
        }

        return new CodePointSet(ranges);
    }

    internal static CodePointSet Of(ReadOnlySpan<(int First, int Last)> ranges)
    {
        CodePointSet result = Empty;
        foreach ((int first, int last) in ranges)
        {
            result = result.Union(Range(first, last));
        }

        return result;
    }

    internal bool Contains(int codePoint)
    {
        int low = 0, high = _ranges.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) >>> 1;
            (int first, int last) = _ranges[mid];
            if (codePoint < first)
            {
                high = mid - 1;
            }
            else if (codePoint > last)
            {
                low = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    internal CodePointSet Union(CodePointSet other)
    {
        List<(int First, int Last)> all = new(_ranges.Count + other._ranges.Count);
        all.AddRange(_ranges);
        all.AddRange(other._ranges);
        all.Sort(static (a, b) => a.First.CompareTo(b.First));
        List<(int, int)> merged = [];
        foreach ((int first, int last) in all)
        {
            if (merged.Count > 0 && first <= merged[^1].Item2 + 1)
            {
                (int f, int l) = merged[^1];
                merged[^1] = (f, Math.Max(l, last));
            }
            else
            {
                merged.Add((first, last));
            }
        }

        return new CodePointSet(merged);
    }

    /// <summary>Every scalar value not in this set.</summary>
    internal CodePointSet Complement()
    {
        List<(int, int)> gaps = [];
        int next = 0;
        foreach ((int first, int last) in _ranges)
        {
            if (first > next)
            {
                gaps.AddRange(Range(next, first - 1)._ranges);
            }

            next = last + 1;
        }

        if (next <= MaxCodePoint)
        {
            gaps.AddRange(Range(next, MaxCodePoint)._ranges);
        }

        return new CodePointSet(gaps);
    }

    internal CodePointSet Subtract(CodePointSet other) => Intersect(other.Complement());

    internal CodePointSet Intersect(CodePointSet other) => Complement().Union(other.Complement()).Complement();

    /// <summary>
    /// The set in .NET's dialect: one atom that matches exactly one member,
    /// a character outside the BMP as its surrogate pair. Never empty text:
    /// the empty set is <c>(?!)</c>.
    /// </summary>
    internal void WriteDotNet(StringBuilder output)
    {
        StringBuilder bmp = new();
        List<string> astral = [];
        foreach ((int first, int last) in _ranges)
        {
            if (first <= 0xFFFF)
            {
                int end = Math.Min(last, 0xFFFF);
                WriteClassChar(bmp, first);
                if (end > first)
                {
                    if (end > first + 1)
                    {
                        bmp.Append('-');
                    }

                    WriteClassChar(bmp, end);
                }
            }

            if (last > 0xFFFF)
            {
                AstralSequences(Math.Max(first, 0x10000), last, astral);
            }
        }

        if (bmp.Length == 0 && astral.Count == 0)
        {
            output.Append("(?!)");
            return;
        }

        if (astral.Count == 0)
        {
            output.Append('[').Append(bmp).Append(']');
            return;
        }

        output.Append("(?:");
        bool firstAlternative = true;
        if (bmp.Length > 0)
        {
            output.Append('[').Append(bmp).Append(']');
            firstAlternative = false;
        }

        foreach (string sequence in astral)
        {
            if (!firstAlternative)
            {
                output.Append('|');
            }

            output.Append(sequence);
            firstAlternative = false;
        }

        output.Append(')');
    }

    /// <summary>A character as a .NET escape that means it in a class or outside one.</summary>
    internal static void WriteClassChar(StringBuilder output, int codeUnit) =>
        output.Append("\\u").Append(codeUnit.ToString("X4", CultureInfo.InvariantCulture));

    /// <summary>A range of supplementary code points as alternatives of surrogate pairs, high surrogate by high surrogate.</summary>
    private static void AstralSequences(int first, int last, List<string> output)
    {
        int highFirst = High(first), highLast = High(last);
        if (highFirst == highLast)
        {
            output.Add(Pair(highFirst, Low(first), Low(last)));
            return;
        }

        output.Add(Pair(highFirst, Low(first), 0xDFFF));
        if (highLast - highFirst > 1)
        {
            StringBuilder middle = new();
            middle.Append('[');
            WriteClassChar(middle, highFirst + 1);
            middle.Append('-');
            WriteClassChar(middle, highLast - 1);
            middle.Append("][");
            WriteClassChar(middle, 0xDC00);
            middle.Append('-');
            WriteClassChar(middle, 0xDFFF);
            middle.Append(']');
            output.Add(middle.ToString());
        }

        output.Add(Pair(highLast, 0xDC00, Low(last)));

        static int High(int codePoint) => 0xD800 + ((codePoint - 0x10000) >> 10);

        static int Low(int codePoint) => 0xDC00 + ((codePoint - 0x10000) & 0x3FF);

        static string Pair(int high, int lowFirst, int lowLast)
        {
            StringBuilder pair = new();
            WriteClassChar(pair, high);
            if (lowFirst == lowLast)
            {
                WriteClassChar(pair, lowFirst);
            }
            else
            {
                pair.Append('[');
                WriteClassChar(pair, lowFirst);
                pair.Append('-');
                WriteClassChar(pair, lowLast);
                pair.Append(']');
            }

            return pair.ToString();
        }
    }
}
