// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Iri;

/// <summary>
/// The five syntax components of a validated IRI reference, as byte ranges into
/// the input (RFC 3986 §3).
/// </summary>
/// <remarks>
/// A plain struct rather than a <c>ref struct</c>, so it can be stored. Nothing
/// is copied: each range indexes the caller's own buffer.
/// <para>
/// An absent component and an empty one are different. <c>http://a</c> has no
/// query; <c>http://a?</c> has an empty one, and the two are different IRIs.
/// The <c>Has*</c> properties carry that distinction, which a zero-length range
/// cannot.
/// </para>
/// </remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
public readonly struct IriComponents
{
    internal IriComponents(
        Range scheme,
        Range authority,
        Range path,
        Range query,
        Range fragment,
        ComponentFlags flags)
    {
        Scheme = scheme;
        Authority = authority;
        Path = path;
        Query = query;
        Fragment = fragment;
        _flags = flags;
    }

    private readonly ComponentFlags _flags;

    /// <summary>The scheme, without the trailing colon.</summary>
    public Range Scheme { get; }

    /// <summary>The authority, without the leading <c>//</c>.</summary>
    public Range Authority { get; }

    /// <summary>The path. Always present; may be empty.</summary>
    public Range Path { get; }

    /// <summary>The query, without the leading <c>?</c>.</summary>
    public Range Query { get; }

    /// <summary>The fragment, without the leading <c>#</c>.</summary>
    public Range Fragment { get; }

    /// <summary>Whether a scheme is present. An IRI is absolute if and only if it is.</summary>
    public bool HasScheme => (_flags & ComponentFlags.Scheme) != 0;

    /// <summary>Whether an authority is present.</summary>
    public bool HasAuthority => (_flags & ComponentFlags.Authority) != 0;

    /// <summary>Whether a query is present, empty or not.</summary>
    public bool HasQuery => (_flags & ComponentFlags.Query) != 0;

    /// <summary>Whether a fragment is present, empty or not.</summary>
    public bool HasFragment => (_flags & ComponentFlags.Fragment) != 0;

    [Flags]
    internal enum ComponentFlags : byte
    {
        None = 0,
        Scheme = 1,
        Authority = 2,
        Query = 4,
        Fragment = 8,
    }
}
