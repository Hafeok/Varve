// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>How a run of the object-store contract suite is made (ADR 0183).</summary>
public sealed record VerificationOptions
{
    /// <summary>The rounds of each race; eight unless set.</summary>
    public VerificationRounds Rounds { get; init; } = VerificationRounds.Default;

    /// <summary>The host's randomness, which names the run's scratch prefix (ADR 0011).</summary>
    public required IProbeRandomness Randomness { get; init; }

    /// <summary>The host's clock, which dates the verification record (ADR 0011).</summary>
    public required TimeProvider Clock { get; init; }
}
