// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Protocol.Model;

/// <summary>An instance's name in its fleet, as its writer record names it (ADR 0168).</summary>
public readonly record struct WriterName
{
    /// <summary>A name: printable ASCII, at most 256 characters.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty, too long or not printable ASCII.</exception>
    public WriterName(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        if (value.Length > 256 || value.AsSpan().ContainsAnyExceptInRange(' ', '~'))
        {
            throw new ArgumentException("An instance's name is printable ASCII, at most 256 characters.", nameof(value));
        }

        Value = value;
    }

    /// <summary>The name.</summary>
    public string Value { get; }

    /// <summary>Renders as the name.</summary>
    public override string ToString() => Value;
}

/// <summary>What a write is to: one dataset, or the system dataset.</summary>
/// <param name="Dataset">The dataset, or none for the system dataset.</param>
public readonly record struct WriterTarget(DatasetName? Dataset)
{
    /// <summary>The fleet's system dataset.</summary>
    public static WriterTarget System => new(null);

    /// <summary>Whether it is the system dataset.</summary>
    public bool IsSystem => Dataset is null;
}

/// <summary>How a route to a writer reads.</summary>
public enum WriterRouteKind : byte
{
    /// <summary>This instance writes it.</summary>
    Local = 0,

    /// <summary>Another instance writes it, at the address its claim recorded.</summary>
    Remote = 1,

    /// <summary>No instance holds the epoch now: it was released and not claimed again.</summary>
    NoWriter = 2,
}

/// <summary>Who writes a dataset (ADR 0168).</summary>
/// <param name="Kind">Local, remote, or no writer.</param>
/// <param name="Address">The writer's internal address, for a remote one.</param>
/// <param name="Epoch">The epoch the route was read at.</param>
[DesignDecision(typeof(WriteRoutingByForwarding.WriterDirectorySeam), Scope = ExceptionScope.Boundary)]
public readonly record struct WriterRoute(WriterRouteKind Kind, string? Address, long Epoch)
{
    /// <summary>This instance writes it.</summary>
    public static WriterRoute Local { get; } = new(WriterRouteKind.Local, null, 0);
}
