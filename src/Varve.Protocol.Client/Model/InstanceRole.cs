// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// What an instance is for a dataset or for the system dataset (ADRs 0163,
/// 0164): its writer, a follower, superseded by a newer writer, or holding
/// none of it, as readiness and the management routes write it.
/// </summary>
/// <remarks>
/// A value the client does not know is carried as the server wrote it.
/// </remarks>
public readonly record struct InstanceRole
{
    /// <summary>The role <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public InstanceRole(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The instance writes it.</summary>
    public static InstanceRole Writer { get; } = new("writer");

    /// <summary>The instance follows it, and forwards its writes (ADR 0168).</summary>
    public static InstanceRole Follower { get; } = new("follower");

    /// <summary>A newer writer holds it; this instance serves reads only.</summary>
    public static InstanceRole Superseded { get; } = new("superseded");

    /// <summary>The dataset is placed on another instance, and this one holds none of it.</summary>
    public static InstanceRole Elsewhere { get; } = new("elsewhere");

    /// <summary>The value as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
