// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Iri;

/// <summary>
/// The contents of an RFC 3986 §3.2.2 <c>IP-literal</c>, between the brackets.
/// </summary>
/// <remarks>
/// Validated structurally rather than by character class, because an address
/// like <c>[1:2:3:4:5:6:7:8:9]</c> passes every character test and is not an
/// address. Nothing in the N-Triples or N-Quads suites reaches here; it is
/// implemented properly so that no limit has to be stated for it.
/// </remarks>
internal static class IpLiteral
{
    internal static bool TryValidate(ReadOnlySpan<byte> inner, out int badOffset)
    {
        badOffset = 0;

        if (inner.Length == 0)
        {
            return false;
        }

        return inner[0] is (byte)'v' or (byte)'V'
            ? TryValidateFuture(inner, out badOffset)
            : TryValidateIpv6(inner, out badOffset);
    }

    /// <summary><c>IPvFuture = "v" 1*HEXDIG "." 1*( unreserved / sub-delims / ":" )</c>.</summary>
    private static bool TryValidateFuture(ReadOnlySpan<byte> inner, out int badOffset)
    {
        int i = 1;
        int hexStart = i;

        while (i < inner.Length && IriChars.IsHex(inner[i]))
        {
            i++;
        }

        if (i == hexStart || i >= inner.Length || inner[i] != (byte)'.')
        {
            badOffset = i;
            return false;
        }

        i++;
        int tailStart = i;

        while (i < inner.Length
               && (IriChars.IsUnreservedAscii(inner[i])
                   || IriChars.IsSubDelim(inner[i])
                   || inner[i] == (byte)':'))
        {
            i++;
        }

        badOffset = i;
        return i == inner.Length && i > tailStart;
    }

    /// <summary>
    /// <c>IPv6address</c>, by the only tractable reading of its nine
    /// alternatives: at most one <c>"::"</c>, at most eight 16-bit groups, and
    /// a trailing IPv4 address counting as two.
    /// </summary>
    private static bool TryValidateIpv6(ReadOnlySpan<byte> inner, out int badOffset)
    {
        badOffset = 0;

        int doubleColon = IndexOfDoubleColon(inner);

        if (doubleColon >= 0 && ContainsDoubleColon(inner[(doubleColon + 2)..]))
        {
            badOffset = doubleColon + 2;
            return false;
        }

        ReadOnlySpan<byte> left = doubleColon < 0 ? inner : inner[..doubleColon];
        ReadOnlySpan<byte> right = doubleColon < 0 ? default : inner[(doubleColon + 2)..];

        if (!TryCountGroups(left, out int leftGroups, out bool leftIpv4))
        {
            return false;
        }

        if (leftIpv4 && doubleColon >= 0)
        {
            // An embedded IPv4 address is only ever the last element.
            return false;
        }

        if (!TryCountGroups(right, out int rightGroups, out _))
        {
            return false;
        }

        int total = leftGroups + rightGroups;

        return doubleColon < 0 ? total == 8 : total <= 7;
    }

    private static int IndexOfDoubleColon(ReadOnlySpan<byte> span)
    {
        for (int i = 0; i + 1 < span.Length; i++)
        {
            if (span[i] == (byte)':' && span[i + 1] == (byte)':')
            {
                return i;
            }
        }

        return -1;
    }

    private static bool ContainsDoubleColon(ReadOnlySpan<byte> span) => IndexOfDoubleColon(span) >= 0;

    /// <summary>
    /// Counts colon-separated groups. A final IPv4 address counts as two, per
    /// the <c>ls32</c> production.
    /// </summary>
    private static bool TryCountGroups(ReadOnlySpan<byte> span, out int groups, out bool endsWithIpv4)
    {
        groups = 0;
        endsWithIpv4 = false;

        if (span.Length == 0)
        {
            return true;
        }

        int i = 0;

        while (true)
        {
            int start = i;

            while (i < span.Length && span[i] != (byte)':')
            {
                i++;
            }

            ReadOnlySpan<byte> group = span[start..i];

            if (i >= span.Length && group.IndexOf((byte)'.') >= 0)
            {
                if (!IsIpv4(group))
                {
                    return false;
                }

                groups += 2;
                endsWithIpv4 = true;
                return true;
            }

            if (group.Length is 0 or > 4)
            {
                return false;
            }

            foreach (byte b in group)
            {
                if (!IriChars.IsHex(b))
                {
                    return false;
                }
            }

            groups++;

            if (i >= span.Length)
            {
                return true;
            }

            i++;

            if (i >= span.Length)
            {
                // A trailing single colon is not a group separator.
                return false;
            }
        }
    }

    /// <summary><c>dec-octet</c> ×4, where a leading zero is not permitted.</summary>
    private static bool IsIpv4(ReadOnlySpan<byte> span)
    {
        int octets = 0;
        int i = 0;

        while (i <= span.Length)
        {
            int start = i;

            while (i < span.Length && span[i] != (byte)'.')
            {
                i++;
            }

            ReadOnlySpan<byte> octet = span[start..i];

            if (octet.Length is 0 or > 3)
            {
                return false;
            }

            if (octet.Length > 1 && octet[0] == (byte)'0')
            {
                return false;
            }

            int value = 0;

            foreach (byte b in octet)
            {
                if (!IriChars.IsDigit(b))
                {
                    return false;
                }

                value = (value * 10) + (b - (byte)'0');
            }

            if (value > 255)
            {
                return false;
            }

            octets++;

            if (i >= span.Length)
            {
                break;
            }

            i++;
        }

        return octets == 4;
    }
}
