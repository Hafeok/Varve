// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Varve.Xsd;

/// <summary>
/// The general categories of XML Schema's category escapes (<c>\p{Lu}</c>,
/// <c>\p{L}</c>, …), as exact sets of scalar values over every plane, read
/// from the runtime's own Unicode data (<see cref="CharUnicodeInfo"/>) once,
/// on the first pattern that needs one. .NET's own <c>\p{…}</c> classifies
/// UTF-16 code units, so a character outside the BMP would never match it
/// (ADR 0153).
/// </summary>
internal sealed class UnicodeCategories
{
    private static readonly Lazy<UnicodeCategories> Instance = new(Build);

    private readonly Dictionary<string, CodePointSet> _byName;

    private UnicodeCategories(Dictionary<string, CodePointSet> byName) => _byName = byName;

    internal static bool TryGet(string name, out CodePointSet set)
    {
        if (!IsName(name))
        {
            set = CodePointSet.Empty;
            return false;
        }

        set = Instance.Value._byName[name];
        return true;
    }

    /// <summary>XML Schema 1.1 Part 2 §G.4.2.4's category names, without building anything.</summary>
    internal static bool IsName(string name) => name switch
    {
        "L" or "Lu" or "Ll" or "Lt" or "Lm" or "Lo"
            or "M" or "Mn" or "Mc" or "Me"
            or "N" or "Nd" or "Nl" or "No"
            or "P" or "Pc" or "Pd" or "Ps" or "Pe" or "Pi" or "Pf" or "Po"
            or "Z" or "Zs" or "Zl" or "Zp"
            or "S" or "Sm" or "Sc" or "Sk" or "So"
            or "C" or "Cc" or "Cf" or "Cs" or "Co" or "Cn" => true,
        _ => false,
    };

    private static UnicodeCategories Build()
    {
        // One pass over every scalar value; runs of one category become one range.
        Dictionary<UnicodeCategory, List<(int, int)>> runs = [];
        int start = 0;
        UnicodeCategory current = CharUnicodeInfo.GetUnicodeCategory(0);
        for (int codePoint = 1; codePoint <= CodePointSet.MaxCodePoint + 1; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            UnicodeCategory category = codePoint > CodePointSet.MaxCodePoint
                ? (UnicodeCategory)(-1)
                : CharUnicodeInfo.GetUnicodeCategory(codePoint);
            if (category != current || codePoint == 0xE000)
            {
                int end = codePoint == 0xE000 ? 0xD7FF : codePoint - 1;
                if (!runs.TryGetValue(current, out List<(int, int)>? list))
                {
                    list = [];
                    runs[current] = list;
                }

                list.Add((start, end));
                start = codePoint;
                current = category;
            }
        }

        Dictionary<string, CodePointSet> byName = new(StringComparer.Ordinal);
        foreach (UnicodeCategory category in Enum.GetValues<UnicodeCategory>())
        {
            byName[Name(category)] = runs.TryGetValue(category, out List<(int, int)>? list)
                ? CodePointSet.Of(list.ToArray())
                : CodePointSet.Empty;
        }

        foreach (string major in (ReadOnlySpan<string>)["L", "M", "N", "P", "Z", "S", "C"])
        {
            CodePointSet union = CodePointSet.Empty;
            foreach (KeyValuePair<string, CodePointSet> entry in byName)
            {
                if (entry.Key.Length == 2 && entry.Key[0] == major[0])
                {
                    union = union.Union(entry.Value);
                }
            }

            byName[major] = union;
        }

        return new UnicodeCategories(byName);
    }

    private static string Name(UnicodeCategory category) => category switch
    {
        UnicodeCategory.UppercaseLetter => "Lu",
        UnicodeCategory.LowercaseLetter => "Ll",
        UnicodeCategory.TitlecaseLetter => "Lt",
        UnicodeCategory.ModifierLetter => "Lm",
        UnicodeCategory.OtherLetter => "Lo",
        UnicodeCategory.NonSpacingMark => "Mn",
        UnicodeCategory.SpacingCombiningMark => "Mc",
        UnicodeCategory.EnclosingMark => "Me",
        UnicodeCategory.DecimalDigitNumber => "Nd",
        UnicodeCategory.LetterNumber => "Nl",
        UnicodeCategory.OtherNumber => "No",
        UnicodeCategory.SpaceSeparator => "Zs",
        UnicodeCategory.LineSeparator => "Zl",
        UnicodeCategory.ParagraphSeparator => "Zp",
        UnicodeCategory.Control => "Cc",
        UnicodeCategory.Format => "Cf",
        UnicodeCategory.Surrogate => "Cs",
        UnicodeCategory.PrivateUse => "Co",
        UnicodeCategory.ConnectorPunctuation => "Pc",
        UnicodeCategory.DashPunctuation => "Pd",
        UnicodeCategory.OpenPunctuation => "Ps",
        UnicodeCategory.ClosePunctuation => "Pe",
        UnicodeCategory.InitialQuotePunctuation => "Pi",
        UnicodeCategory.FinalQuotePunctuation => "Pf",
        UnicodeCategory.OtherPunctuation => "Po",
        UnicodeCategory.MathSymbol => "Sm",
        UnicodeCategory.CurrencySymbol => "Sc",
        UnicodeCategory.ModifierSymbol => "Sk",
        UnicodeCategory.OtherSymbol => "So",
        _ => "Cn",
    };
}
