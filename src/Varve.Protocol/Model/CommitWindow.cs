// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Protocol.Model;

/// <summary>
/// A number of a dataset's last commits: how far back a writer honours a
/// client idempotency key (ADR 0246). Counted in commits of the log, never
/// in time, so every writer of the dataset agrees on it.
/// </summary>
public readonly record struct CommitWindow
{
    /// <summary>The default: the last 100 000 commits.</summary>
    public static CommitWindow Default => new(100_000);

    /// <summary>A window of <paramref name="commits"/> commits.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="commits"/> is below 1.</exception>
    public CommitWindow(long commits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(commits, 1);
        Commits = commits;
    }

    /// <summary>The number of commits.</summary>
    public long Commits { get; }

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Commits.ToString(CultureInfo.InvariantCulture);
}
