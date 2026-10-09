// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Varve.Iri;

namespace Varve.Protocol.Client;

/// <summary>
/// Which outbound addresses a <c>SERVICE</c>, a <c>LOAD</c> or a client call
/// may reach (ADR 0103). Decided on the IRI as written, before any connection
/// is opened: an allow-list of IRI prefixes that is empty by default, only
/// <c>http</c> and <c>https</c>, no credentials in the authority, and no
/// loopback, link-local, private or unspecified address unless the operator
/// says so.
/// </summary>
/// <remarks>
/// The policy sees names, not what they resolve to: a name that resolves to a
/// private address is the operator's responsibility when they allow it, and
/// rebinding between this check and the connection is not defended here. A
/// redirect is refused by the clients, because it is an address this policy
/// did not see.
/// </remarks>
public sealed class EndpointPolicy
{
    private readonly string[] _prefixes;

    /// <summary>A policy allowing <paramref name="allowedPrefixes"/>, each an absolute IRI prefix compared by its bytes.</summary>
    /// <exception cref="ArgumentException">A prefix is empty or not an <c>http</c> or <c>https</c> IRI.</exception>
    public EndpointPolicy(IReadOnlyList<string> allowedPrefixes, bool allowPrivateAddresses)
    {
        ArgumentNullException.ThrowIfNull(allowedPrefixes);
        _prefixes = new string[allowedPrefixes.Count];

        for (int i = 0; i < _prefixes.Length; i++)
        {
            string prefix = allowedPrefixes[i];

            if (string.IsNullOrWhiteSpace(prefix)
                || !(prefix.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("An allowed prefix is an absolute http or https IRI prefix: " + prefix, nameof(allowedPrefixes));
            }

            _prefixes[i] = prefix;
        }

        AllowPrivateAddresses = allowPrivateAddresses;
    }

    /// <summary>The policy that allows nothing: the default of every host (ADRs 0055, 0057, 0103).</summary>
    public static EndpointPolicy None { get; } = new([], allowPrivateAddresses: false);

    /// <summary>The prefixes an endpoint may start with.</summary>
    public IReadOnlyList<string> AllowedPrefixes => _prefixes;

    /// <summary>Whether loopback, link-local, private and unspecified addresses, and <c>localhost</c>, may be reached.</summary>
    public bool AllowPrivateAddresses { get; }

    /// <summary>Whether <paramref name="iri"/> may be reached, and if not, why not.</summary>
    public bool Allows(ReadOnlySpan<byte> iri, [NotNullWhen(false)] out string? reason)
    {
        if (!IriRef.TryValidate(iri, out IriComponents parts, out _) || !parts.HasScheme || !parts.HasAuthority)
        {
            reason = "not an absolute IRI with an authority";
            return false;
        }

        ReadOnlySpan<byte> scheme = iri[parts.Scheme];
        bool http = scheme.Length == 4 && Ascii.EqualsIgnoreCase(scheme, "http"u8);
        bool https = scheme.Length == 5 && Ascii.EqualsIgnoreCase(scheme, "https"u8);

        if (!http && !https)
        {
            reason = "the scheme is not http or https";
            return false;
        }

        ReadOnlySpan<byte> authority = iri[parts.Authority];

        if (authority.IndexOf((byte)'@') >= 0)
        {
            reason = "the authority carries credentials";
            return false;
        }

        if (!Listed(Encoding.UTF8.GetString(iri)))
        {
            reason = _prefixes.Length == 0
                ? "no endpoint is allowed; the policy's allow-list is empty"
                : "not under an allowed prefix";
            return false;
        }

        if (!AllowPrivateAddresses && IsPrivate(Host(authority)))
        {
            reason = "a loopback, link-local, private or unspecified address, refused unless the policy allows private addresses";
            return false;
        }

        reason = null;
        return true;
    }

    private bool Listed(string iri)
    {
        foreach (string prefix in _prefixes)
        {
            if (iri.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // RFC 3986 §3.2: host, with a bracketed IPv6 literal, then an optional port.
    private static ReadOnlySpan<byte> Host(ReadOnlySpan<byte> authority)
    {
        if (authority.Length > 0 && authority[0] == (byte)'[')
        {
            int close = authority.IndexOf((byte)']');
            return close > 0 ? authority[1..close] : authority;
        }

        int colon = authority.LastIndexOf((byte)':');
        return colon >= 0 ? authority[..colon] : authority;
    }

    private static bool IsPrivate(ReadOnlySpan<byte> host)
    {
        if (host.Length == 0)
        {
            return true;
        }

        string name = Encoding.UTF8.GetString(host);

        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(name, out IPAddress? address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> bytes = stackalloc byte[4];
            address.TryWriteBytes(bytes, out _);
            return bytes[0] == 127
                || bytes[0] == 10
                || bytes[0] == 0
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        return IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal
            || address.IsIPv6UniqueLocal
            || address.IsIPv6SiteLocal;
    }
}
