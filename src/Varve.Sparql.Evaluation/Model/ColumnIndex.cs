// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Sparql.Evaluation.Model;

/// <summary>
/// A column of a <c>SELECT</c>'s solutions: the position of its variable in
/// <see cref="SolutionResults.Variables"/>.
/// </summary>
/// <remarks>
/// ADR 0069, amended 2026-10-01 (<c>ColumnIndexIsAWrapper</c>). There is no
/// conversion to or from <c>int</c> but the constructor and
/// <see cref="Value"/> (DD0015).
/// </remarks>
public readonly record struct ColumnIndex
{
    /// <summary>A column, counted from zero.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public ColumnIndex(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The column's position, counted from zero.</summary>
    public int Value { get; }

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
