// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Store.Log;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// What a run of the object-store contract suite found (ADR 0183): a pass,
/// or the first case the store broke.
/// </summary>
public sealed record VerificationOutcome
{
    /// <summary>The suite that ran.</summary>
    public required VerificationSuite Suite { get; init; }

    /// <summary>The rounds of each race.</summary>
    public required VerificationRounds Rounds { get; init; }

    /// <summary>The cases that passed, in the order they ran.</summary>
    public required IReadOnlyList<VerificationCaseName> Passed { get; init; }

    /// <summary>The first case the store broke, or none: the run stops there.</summary>
    public VerificationCounterexample? Counterexample { get; init; }

    /// <summary>Whether the store kept the contract in every case.</summary>
    public bool Verified => Counterexample is null;

    /// <summary>The scratch objects the run could not remove; nothing reads them again.</summary>
    public required IReadOnlyList<ObjectName> LeftBehind { get; init; }

    /// <summary>The verification record the run wrote at the store's root, when it passed.</summary>
    public VerificationRecord? Record { get; init; }
}

/// <summary>A case of the suite, by the sentence that states it.</summary>
/// <param name="Value">The sentence.</param>
public readonly record struct VerificationCaseName(string Value)
{
    /// <summary>Renders as the sentence.</summary>
    public override string ToString() => Value;
}

/// <summary>What a store did that the contract forbids.</summary>
/// <param name="Value">What was seen.</param>
public readonly record struct ContractViolationText(string Value)
{
    /// <summary>Renders as what was seen.</summary>
    public override string ToString() => Value;
}

/// <summary>The case a store broke, and what was seen.</summary>
/// <param name="Case">The case.</param>
/// <param name="Violation">What the store did.</param>
public sealed record VerificationCounterexample(VerificationCaseName Case, ContractViolationText Violation);
