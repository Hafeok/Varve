using System;

namespace Varve.Iri;

/// <summary>
/// Recursive-descent validation of an RFC 3987 <c>IRI-reference</c>.
/// </summary>
/// <remarks>
/// One left-to-right pass, no backtracking except the scheme probe, and no
/// allocation. Components are recorded as byte ranges into the caller's input.
/// </remarks>
internal static class IriScanner
{
    /// <summary>Where a character class is being applied. Each permits a different set.</summary>
    private enum Context
    {
        /// <summary><c>iunreserved / pct-encoded / sub-delims / ":" / "@"</c>.</summary>
        Path,

        /// <summary>Path, plus <c>iprivate</c>, <c>"/"</c> and <c>"?"</c>.</summary>
        Query,

        /// <summary>Path, plus <c>"/"</c> and <c>"?"</c>.</summary>
        Fragment,

        /// <summary><c>iunreserved / pct-encoded / sub-delims</c> — no colon.</summary>
        RegName,

        /// <summary>Registered name, plus <c>":"</c>.</summary>
        UserInfo,
    }

    internal static bool TryValidate(
        ReadOnlySpan<byte> utf8,
        out IriComponents components,
        out IriError error)
    {
        components = default;
        error = default;

        int i = 0;
        IriComponents.ComponentFlags flags = IriComponents.ComponentFlags.None;
        Range scheme = default;
        Range authority = default;
        Range query = default;
        Range fragment = default;

        int colon = ProbeScheme(utf8);
        if (colon > 0)
        {
            scheme = new Range(0, colon);
            flags |= IriComponents.ComponentFlags.Scheme;
            i = colon + 1;
        }

        if (i + 1 < utf8.Length && utf8[i] == (byte)'/' && utf8[i + 1] == (byte)'/')
        {
            i += 2;
            int authorityStart = i;

            if (!TryScanAuthority(utf8, ref i, out error))
            {
                return false;
            }

            authority = new Range(authorityStart, i);
            flags |= IriComponents.ComponentFlags.Authority;
        }

        int pathStart = i;
        bool relativeFirstSegment =
            (flags & IriComponents.ComponentFlags.Scheme) == 0
            && (flags & IriComponents.ComponentFlags.Authority) == 0
            && (i >= utf8.Length || utf8[i] != (byte)'/');

        if (!TryScanPath(utf8, ref i, relativeFirstSegment, out error))
        {
            return false;
        }

        Range path = new(pathStart, i);

        if (i < utf8.Length && utf8[i] == (byte)'?')
        {
            i++;
            int queryStart = i;

            if (!TryScanRun(utf8, ref i, Context.Query, out error))
            {
                return false;
            }

            query = new Range(queryStart, i);
            flags |= IriComponents.ComponentFlags.Query;
        }

        if (i < utf8.Length && utf8[i] == (byte)'#')
        {
            i++;
            int fragmentStart = i;

            if (!TryScanRun(utf8, ref i, Context.Fragment, out error))
            {
                return false;
            }

            fragment = new Range(fragmentStart, i);
            flags |= IriComponents.ComponentFlags.Fragment;
        }

        if (i != utf8.Length)
        {
            error = new IriError(IriErrorKind.InvalidCharacter, i);
            return false;
        }

        components = new IriComponents(scheme, authority, path, query, fragment, flags);
        return true;
    }

    /// <summary>
    /// Returns the index of the scheme's colon, or -1. A scheme exists only
    /// when the input starts <c>ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) ":"</c>
    /// — anything else means the colon, if any, belongs to the path.
    /// </summary>
    private static int ProbeScheme(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length == 0 || !IriChars.IsAlpha(utf8[0]))
        {
            return -1;
        }

        for (int i = 1; i < utf8.Length; i++)
        {
            if (utf8[i] == (byte)':')
            {
                return i;
            }

            if (!IriChars.IsSchemeTail(utf8[i]))
            {
                return -1;
            }
        }

        return -1;
    }

    private static bool TryScanAuthority(ReadOnlySpan<byte> utf8, ref int i, out IriError error)
    {
        int start = i;
        int end = i;

        while (end < utf8.Length
               && utf8[end] != (byte)'/'
               && utf8[end] != (byte)'?'
               && utf8[end] != (byte)'#')
        {
            end++;
        }

        ReadOnlySpan<byte> authority = utf8[start..end];

        // The last '@' separates userinfo from host: a '@' may appear in
        // userinfo, so the first one is not necessarily the separator.
        int at = authority.LastIndexOf((byte)'@');
        int hostStart = start;

        if (at >= 0)
        {
            int userInfoEnd = start + at;
            int scan = start;

            if (!TryScanRun(utf8[..userInfoEnd], ref scan, Context.UserInfo, out error) || scan != userInfoEnd)
            {
                error = error.IsError ? error : new IriError(IriErrorKind.InvalidCharacter, scan);
                return false;
            }

            hostStart = userInfoEnd + 1;
        }

        if (!TryScanHostAndPort(utf8, hostStart, end, out error))
        {
            return false;
        }

        i = end;
        return true;
    }

    private static bool TryScanHostAndPort(ReadOnlySpan<byte> utf8, int start, int end, out IriError error)
    {
        error = default;

        if (start < end && utf8[start] == (byte)'[')
        {
            int close = utf8[start..end].IndexOf((byte)']');

            if (close < 0)
            {
                error = new IriError(IriErrorKind.UnterminatedIpLiteral, start);
                return false;
            }

            close += start;

            if (!IpLiteral.TryValidate(utf8[(start + 1)..close], out int badOffset))
            {
                error = new IriError(IriErrorKind.InvalidCharacter, start + 1 + badOffset);
                return false;
            }

            return TryScanPort(utf8, close + 1, end, out error);
        }

        // Outside an IP-literal a colon can only be the port separator, because
        // ireg-name does not admit one.
        int colon = utf8[start..end].LastIndexOf((byte)':');
        int hostEnd = colon < 0 ? end : start + colon;

        int scan = start;

        if (!TryScanRun(utf8[..hostEnd], ref scan, Context.RegName, out error) || scan != hostEnd)
        {
            error = error.IsError ? error : new IriError(IriErrorKind.InvalidCharacter, scan);
            return false;
        }

        return TryScanPort(utf8, hostEnd, end, out error);
    }

    private static bool TryScanPort(ReadOnlySpan<byte> utf8, int start, int end, out IriError error)
    {
        error = default;

        if (start >= end)
        {
            return true;
        }

        if (utf8[start] != (byte)':')
        {
            error = new IriError(IriErrorKind.InvalidCharacter, start);
            return false;
        }

        for (int i = start + 1; i < end; i++)
        {
            if (!IriChars.IsDigit(utf8[i]))
            {
                error = new IriError(IriErrorKind.InvalidPort, i);
                return false;
            }
        }

        return true;
    }

    private static bool TryScanPath(
        ReadOnlySpan<byte> utf8,
        ref int i,
        bool relativeFirstSegment,
        out IriError error)
    {
        error = default;

        while (i < utf8.Length)
        {
            byte b = utf8[i];

            if (b == (byte)'?' || b == (byte)'#')
            {
                return true;
            }

            if (b == (byte)'/')
            {
                // Only the first segment of a path-noscheme excludes a colon.
                relativeFirstSegment = false;
                i++;
                continue;
            }

            if (relativeFirstSegment && b == (byte)':')
            {
                error = new IriError(IriErrorKind.InvalidCharacter, i);
                return false;
            }

            if (!TryScanOne(utf8, ref i, Context.Path, out error))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryScanRun(ReadOnlySpan<byte> utf8, ref int i, Context context, out IriError error)
    {
        error = default;

        while (i < utf8.Length)
        {
            byte b = utf8[i];

            if (context == Context.Query && b == (byte)'#')
            {
                return true;
            }

            if ((context == Context.Path || context == Context.RegName || context == Context.UserInfo)
                && (b == (byte)'?' || b == (byte)'#'))
            {
                return true;
            }

            if (!TryScanOne(utf8, ref i, context, out error))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryScanOne(ReadOnlySpan<byte> utf8, ref int i, Context context, out IriError error)
    {
        error = default;
        byte b = utf8[i];

        if (b == (byte)'%')
        {
            if (i + 2 >= utf8.Length || !IriChars.IsHex(utf8[i + 1]) || !IriChars.IsHex(utf8[i + 2]))
            {
                error = new IriError(
                    i + 2 >= utf8.Length ? IriErrorKind.UnexpectedEnd : IriErrorKind.InvalidPercentEncoding,
                    i);
                return false;
            }

            i += 3;
            return true;
        }

        if (b < 128)
        {
            if (IsAsciiAllowed(b, context))
            {
                i++;
                return true;
            }

            error = new IriError(IriErrorKind.InvalidCharacter, i);
            return false;
        }

        if (!IriChars.TryDecode(utf8[i..], out int scalar, out int length))
        {
            error = new IriError(IriErrorKind.InvalidUtf8, i);
            return false;
        }

        bool allowed = IriChars.IsUcsChar(scalar)
            || (context == Context.Query && IriChars.IsPrivate(scalar));

        if (!allowed)
        {
            error = new IriError(IriErrorKind.InvalidCharacter, i);
            return false;
        }

        i += length;
        return true;
    }

    private static bool IsAsciiAllowed(byte b, Context context)
    {
        if (IriChars.IsUnreservedAscii(b) || IriChars.IsSubDelim(b))
        {
            return true;
        }

        return context switch
        {
            Context.Path => b == (byte)':' || b == (byte)'@',
            Context.Query or Context.Fragment =>
                b == (byte)':' || b == (byte)'@' || b == (byte)'/' || b == (byte)'?',
            Context.UserInfo => b == (byte)':',
            _ => false,
        };
    }
}
