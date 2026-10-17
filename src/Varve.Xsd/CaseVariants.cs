// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Varve.Xsd;

/// <summary>
/// F&amp;O 3.1 §5.6.2's case-variants under the <c>i</c> flag: C2 is a
/// case-variant of C1 when <c>lower-case(C1) eq lower-case(C2)</c> or
/// <c>upper-case(C1) eq upper-case(C2)</c>, both mappings single characters.
/// Read from the runtime's invariant casing once, on the first pattern that
/// needs it. Applied by the translator to characters and ranges only, so
/// <c>\p{Lu}</c> still matches upper-case letters only, as §5.6.2 says.
/// </summary>
internal sealed class CaseVariants
{
    private static readonly Lazy<CaseVariants> Instance = new(Build);

    private readonly int[] _cased;
    private readonly Dictionary<int, List<int>> _byLower;
    private readonly Dictionary<int, List<int>> _byUpper;

    private CaseVariants(int[] cased, Dictionary<int, List<int>> byLower, Dictionary<int, List<int>> byUpper)
    {
        _cased = cased;
        _byLower = byLower;
        _byUpper = byUpper;
    }

    /// <summary>The set with every case-variant of every member added.</summary>
    internal static CodePointSet Close(CodePointSet set)
    {
        CaseVariants variants = Instance.Value;
        List<(int, int)> added = [];
        foreach (int c in variants._cased)
        {
            if (!set.Contains(c))
            {
                continue;
            }

            AddAll(variants._byLower, Lower(c), added);
            AddAll(variants._byUpper, Upper(c), added);
        }

        return added.Count == 0 ? set : set.Union(CodePointSet.Of(added.ToArray()));

        static void AddAll(Dictionary<int, List<int>> map, int key, List<(int, int)> into)
        {
            if (map.TryGetValue(key, out List<int>? members))
            {
                foreach (int member in members)
                {
                    into.Add((member, member));
                }
            }
        }
    }

    private static CaseVariants Build()
    {
        List<int> cased = [];
        Dictionary<int, List<int>> byLower = [];
        Dictionary<int, List<int>> byUpper = [];
        for (int c = 0; c <= CodePointSet.MaxCodePoint; c++)
        {
            if (c is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            int lower = Lower(c), upper = Upper(c);
            if (lower == c && upper == c)
            {
                continue;
            }

            cased.Add(c);
            Add(byLower, lower, c);
            Add(byUpper, upper, c);
            Add(byLower, lower, lower);
            Add(byUpper, upper, upper);
        }

        return new CaseVariants(cased.ToArray(), byLower, byUpper);

        static void Add(Dictionary<int, List<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out List<int>? list))
            {
                list = [];
                map[key] = list;
            }

            if (!list.Contains(value))
            {
                list.Add(value);
            }
        }
    }

    private static int Lower(int c) => Map(c, upper: false);

    private static int Upper(int c) => Map(c, upper: true);

    /// <summary>The invariant single-character case mapping, or the character itself when the mapping is not one character.</summary>
    private static int Map(int c, bool upper)
    {
        if (c <= 0xFFFF)
        {
            char ch = (char)c;
            return upper ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch);
        }

        string text = char.ConvertFromUtf32(c);
        string mapped = upper ? text.ToUpperInvariant() : text.ToLowerInvariant();
        return mapped.Length == 2 && char.IsSurrogatePair(mapped[0], mapped[1])
            ? char.ConvertToUtf32(mapped[0], mapped[1])
            : mapped.Length == 1 ? mapped[0] : c;
    }
}
