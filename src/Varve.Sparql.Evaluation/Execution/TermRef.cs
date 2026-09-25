// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Sparql.Evaluation.Execution;

/// <summary>
/// One slot of a solution: unbound, a source handle, or a local term — an
/// index (from 1) into the execution's own table (<c>sparql-evaluation.md</c> §4.1).
/// </summary>
internal readonly struct TermRef : IEquatable<TermRef>
{
    internal TermRef(ulong raw, bool local)
    {
        Raw = raw;
        IsLocal = local;
    }

    internal static TermRef Unbound => default;

    internal ulong Raw { get; }

    internal bool IsLocal { get; }

    internal bool IsBound => Raw != 0;

    public bool Equals(TermRef other) => Raw == other.Raw && IsLocal == other.IsLocal;

    public override bool Equals(object? obj) => obj is TermRef other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Raw, IsLocal);
}
