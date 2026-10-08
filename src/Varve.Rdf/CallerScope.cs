// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Rdf;

/// <summary>Whether a caller administers the dataset.</summary>
public enum AdminAccess : byte
{
    /// <summary>Not an administrator: the scopes bound the caller.</summary>
    None,

    /// <summary>An administrator, dataset-wide (ADR 0107): reads and writes every graph.</summary>
    Admin,
}

/// <summary>
/// What one caller may do with one dataset (ADR 0107): the graphs it reads,
/// the graphs it writes, and whether it administers the dataset. Resolved by
/// a host from its grants; <c>Varve.Protocol</c> and <c>Varve.Sparql.Store</c>
/// take it as a value and know no claim. (ADR 0107 calls it the access
/// scope; <c>Varve.Store.AccessScope</c> already names a dataset's history
/// setting, so the type is the caller's scope.)
/// </summary>
public sealed class CallerScope
{
    /// <summary>A scope of <paramref name="readable"/> and <paramref name="writable"/> graphs; an admin reads and writes every graph whatever they say.</summary>
    public CallerScope(GraphScope readable, GraphScope writable, AdminAccess admin)
    {
        ArgumentNullException.ThrowIfNull(readable);
        ArgumentNullException.ThrowIfNull(writable);
        IsAdmin = admin == AdminAccess.Admin;
        Readable = IsAdmin ? GraphScope.All : readable;
        Writable = IsAdmin ? GraphScope.All : writable;
    }

    /// <summary>Every graph, read and written, as an administrator: anonymous mode's and the conformance host's.</summary>
    public static CallerScope Everything { get; } = new(GraphScope.All, GraphScope.All, AdminAccess.Admin);

    /// <summary>The graphs the caller reads.</summary>
    public GraphScope Readable { get; }

    /// <summary>The graphs the caller writes.</summary>
    public GraphScope Writable { get; }

    /// <summary>Whether the caller administers the dataset.</summary>
    public bool IsAdmin { get; }
}
