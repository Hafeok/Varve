// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// The verification record at a store's root (ADR 0183): that the contract
/// suite passed against this endpoint, by which suite, and when. It guards
/// against a mistake, not an adversary: whoever can write to the store can
/// write the record.
/// </summary>
public sealed record VerificationRecord
{
    /// <summary>
    /// Whether this build reads the record's format. One it does not read is
    /// not a verification (ADR 0217), and the other members say nothing.
    /// </summary>
    public required bool Understood { get; init; }

    /// <summary>The suite that passed.</summary>
    public VerificationSuite Suite { get; init; } = new(1);

    /// <summary>The rounds of each race.</summary>
    public VerificationRounds Rounds { get; init; } = VerificationRounds.Default;

    /// <summary>When it passed, by the clock of whoever ran it: for an operator to read, never to decide by.</summary>
    public VerificationTime VerifiedAt { get; init; }

    /// <summary>Whether the record is a verification by <paramref name="minimum"/> or a newer suite.</summary>
    public bool Verifies(VerificationSuite minimum) => Understood && Suite >= minimum;
}

/// <summary>
/// When a run of the suite passed, by the clock of whoever ran it: for an
/// operator to read, never for anything to decide by.
/// </summary>
/// <param name="Value">The instant.</param>
public readonly record struct VerificationTime(DateTimeOffset Value);
