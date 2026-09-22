using System;

namespace Varve.Rdf;

/// <summary>
/// Well-formedness of a language tag, per BCP 47 §2.1.
/// </summary>
/// <remarks>
/// <para>
/// RDF 1.2 N-Triples [15] gives <c>LANG_DIR</c> as
/// <c>'@' [a-zA-Z]+ ('-' [a-zA-Z0-9]+)* ('--' [a-zA-Z]+)?</c> and then states,
/// normatively and outside the grammar, that <strong>the language tag MUST be
/// well-formed according to section 2.2.9 of BCP 47</strong>. The grammar alone
/// accepts <c>@cantbethislong</c>; the suite's
/// <c>ntriples-langdir-bad-4</c> requires it to be rejected. This is what makes
/// the difference.
/// </para>
/// <para>
/// <strong>Well-formed, not valid.</strong> BCP 47 §2.2.9 separates the two:
/// well-formed means it matches the ABNF in §2.1, valid additionally means
/// every subtag appears in the IANA registry. RDF asks for the first. The
/// second would mean shipping and ageing a copy of the registry, and would
/// reject a tag registered after our release — a term would stop being a term
/// because a file got old.
/// </para>
/// <para>
/// This is in <c>Varve.Rdf</c> rather than in the parser because the term model
/// is what a language tag belongs to. A writer that can emit a tag the reader
/// must reject is the same asymmetry as a model that can hold a literal no
/// syntax can express.
/// </para>
/// </remarks>
public static class LanguageTag
{
    /// <summary>
    /// The 26 grandfathered tags of BCP 47 §2.1, registered before the current
    /// ABNF existed. Most of the regular ones happen to match
    /// <c>langtag</c> anyway; the irregular ones — <c>en-GB-oed</c>,
    /// <c>i-klingon</c>, <c>sgn-BE-FR</c> and the rest — do not, and a list is
    /// the only way to accept them.
    /// </summary>
    private static readonly string[] Grandfathered =
    [
        "en-GB-oed", "i-ami", "i-bnn", "i-default", "i-enochian", "i-hak",
        "i-klingon", "i-lux", "i-mingo", "i-navajo", "i-pwn", "i-tao",
        "i-tay", "i-tsu", "sgn-BE-FR", "sgn-BE-NL", "sgn-CH-DE",
        "art-lojban", "cel-gaulish", "no-bok", "no-nyn", "zh-guoyu",
        "zh-hakka", "zh-min", "zh-min-nan", "zh-xiang",
    ];

    /// <summary>
    /// Whether <paramref name="tag"/> is a well-formed <c>Language-Tag</c>.
    /// Comparison is case-insensitive, as BCP 47 §2.1.1 requires.
    /// </summary>
    /// <param name="tag">The tag, as UTF-8, without the leading <c>@</c>.</param>
    public static bool IsWellFormed(ReadOnlySpan<byte> tag)
    {
        if (tag.IsEmpty || HasEmptySubtag(tag))
        {
            return false;
        }

        return IsGrandfathered(tag) || IsPrivateUse(new Subtags(tag)) || IsLangtag(new Subtags(tag));
    }

    /// <summary>Whether a base direction is one of the two BCP 47 allows.</summary>
    public static bool IsBaseDirection(ReadOnlySpan<byte> direction) =>
        direction.SequenceEqual("ltr"u8) || direction.SequenceEqual("rtl"u8);

    /// <summary>
    /// A leading, trailing or doubled hyphen. Checked up front rather than in
    /// the walk below, where an empty subtag would be indistinguishable from
    /// the end of the tag and <c>en-</c> would read as <c>en</c>.
    /// </summary>
    private static bool HasEmptySubtag(ReadOnlySpan<byte> tag)
    {
        const byte Hyphen = (byte)'-';

        if (tag[0] == Hyphen || tag[^1] == Hyphen)
        {
            return true;
        }

        for (int i = 1; i < tag.Length; i++)
        {
            if (tag[i] == Hyphen && tag[i - 1] == Hyphen)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGrandfathered(ReadOnlySpan<byte> tag)
    {
        foreach (string candidate in Grandfathered)
        {
            if (EqualsAsciiIgnoreCase(tag, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>privateuse = "x" 1*("-" 1*8alphanum)</c>.</summary>
    private static bool IsPrivateUse(Subtags subtags)
    {
        if (!subtags.Has || subtags.Current.Length != 1 || (subtags.Current[0] | 0x20) != 'x')
        {
            return false;
        }

        subtags.Advance();
        return TakeRun(ref subtags, minimum: 1, maximum: 8) && !subtags.Has;
    }

    /// <summary>
    /// <c>langtag = language ["-" script] ["-" region] *("-" variant)
    /// *("-" extension) ["-" privateuse]</c>.
    /// </summary>
    private static bool IsLangtag(Subtags subtags)
    {
        if (!TakeLanguage(ref subtags))
        {
            return false;
        }

        // script = 4ALPHA
        if (subtags.Has && subtags.Current.Length == 4 && IsAllAlpha(subtags.Current))
        {
            subtags.Advance();
        }

        // region = 2ALPHA / 3DIGIT
        if (subtags.Has
            && ((subtags.Current.Length == 2 && IsAllAlpha(subtags.Current))
                || (subtags.Current.Length == 3 && IsAllDigit(subtags.Current))))
        {
            subtags.Advance();
        }

        // variant = 5*8alphanum / (DIGIT 3alphanum)
        while (subtags.Has && IsVariant(subtags.Current))
        {
            subtags.Advance();
        }

        // extension = singleton 1*("-" 2*8alphanum)
        while (subtags.Has && subtags.Current.Length == 1 && IsSingleton(subtags.Current[0]))
        {
            subtags.Advance();

            if (!TakeRun(ref subtags, minimum: 2, maximum: 8))
            {
                return false;
            }
        }

        if (subtags.Has && subtags.Current.Length == 1 && (subtags.Current[0] | 0x20) == 'x')
        {
            subtags.Advance();

            if (!TakeRun(ref subtags, minimum: 1, maximum: 8))
            {
                return false;
            }
        }

        return !subtags.Has;
    }

    /// <summary><c>language = 2*3ALPHA ["-" extlang] / 4ALPHA / 5*8ALPHA</c>.</summary>
    private static bool TakeLanguage(ref Subtags subtags)
    {
        if (!subtags.Has || !IsAllAlpha(subtags.Current))
        {
            return false;
        }

        int length = subtags.Current.Length;

        if (length is < 2 or > 8)
        {
            return false;
        }

        subtags.Advance();

        if (length > 3)
        {
            return true;
        }

        // extlang = 3ALPHA *2("-" 3ALPHA). A three-letter subtag here can only
        // be an extlang: a variant needs five to eight characters or a leading
        // digit, and a three-character region is all digits. So taking them
        // greedily cannot strand a later production.
        for (int taken = 0;
            taken < 3 && subtags.Has && subtags.Current.Length == 3 && IsAllAlpha(subtags.Current);
            taken++)
        {
            subtags.Advance();
        }

        return true;
    }

    private static bool TakeRun(ref Subtags subtags, int minimum, int maximum)
    {
        int taken = 0;

        while (subtags.Has
            && subtags.Current.Length >= minimum
            && subtags.Current.Length <= maximum
            && IsAllAlphanumeric(subtags.Current))
        {
            subtags.Advance();
            taken++;
        }

        return taken > 0;
    }

    private static bool IsVariant(ReadOnlySpan<byte> subtag) =>
        (subtag.Length is >= 5 and <= 8 && IsAllAlphanumeric(subtag))
        || (subtag.Length == 4 && IsDigit(subtag[0]) && IsAllAlphanumeric(subtag));

    /// <summary><c>singleton</c> is any alphanumeric except <c>x</c> and <c>X</c>.</summary>
    private static bool IsSingleton(byte b) =>
        IsAlphanumeric(b) && (b | 0x20) != 'x';

    private static bool IsAlpha(byte b) => (uint)((b | 0x20) - 'a') <= 'z' - 'a';

    private static bool IsDigit(byte b) => (uint)(b - '0') <= 9;

    private static bool IsAlphanumeric(byte b) => IsAlpha(b) || IsDigit(b);

    private static bool IsAllAlpha(ReadOnlySpan<byte> span)
    {
        foreach (byte b in span)
        {
            if (!IsAlpha(b))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllDigit(ReadOnlySpan<byte> span)
    {
        foreach (byte b in span)
        {
            if (!IsDigit(b))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllAlphanumeric(ReadOnlySpan<byte> span)
    {
        foreach (byte b in span)
        {
            if (!IsAlphanumeric(b))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<byte> utf8, string ascii)
    {
        if (utf8.Length != ascii.Length)
        {
            return false;
        }

        for (int i = 0; i < utf8.Length; i++)
        {
            if ((utf8[i] | 0x20) != (ascii[i] | 0x20))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The hyphen-separated subtags, one at a time, without allocating.</summary>
    private ref struct Subtags
    {
        private readonly ReadOnlySpan<byte> _tag;
        private int _start;
        private int _length;

        internal Subtags(ReadOnlySpan<byte> tag)
        {
            _tag = tag;
            _start = 0;
            _length = 0;
            Has = false;
            Take(0);
        }

        internal bool Has { get; private set; }

        internal readonly ReadOnlySpan<byte> Current => _tag.Slice(_start, _length);

        internal void Advance() => Take(_start + _length + 1);

        private void Take(int from)
        {
            if (from > _tag.Length)
            {
                Has = false;
                return;
            }

            int end = from;

            while (end < _tag.Length && _tag[end] != (byte)'-')
            {
                end++;
            }

            _start = from;
            _length = end - from;

            // An empty subtag is never well-formed, and treating it as the end
            // would make "en-" look like "en".
            Has = _length > 0;
        }
    }
}
