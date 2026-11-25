// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// How many times each race of the contract suite is run (ADR 0183): every
/// round is sixteen clients racing one name, or one version.
/// </summary>
public readonly record struct VerificationRounds
{
    /// <summary>A number of rounds.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is below 1.</exception>
    public VerificationRounds(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Value = value;
    }

    /// <summary>Eight rounds: what a pull request runs.</summary>
    public static VerificationRounds Default => new(8);

    /// <summary>The number.</summary>
    public int Value { get; }

    /// <summary>Renders as the number.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>How many clients race one name, or one version, in each round (ADR 0134 decision 7).</summary>
/// <param name="Value">The number.</param>
public readonly record struct VerificationRacers(int Value)
{
    /// <summary>Renders as the number.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
