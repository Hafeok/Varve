// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// Where a writer's open draws the random name of its conditional-writes
/// probe, <c>probe/{random}</c> (ADR 0134). The host supplies it — the server
/// from the operating system's generator — because the store never draws
/// randomness of its own (ADR 0011). Nothing in the log depends on it.
/// </summary>
[Contract(typeof(TheObjectStoreContract.ConditionalWritesProbedAtOpen), Role = "the random source a writer's open names its probe key from")]
public interface IProbeRandomness
{
    /// <summary>Fills <paramref name="destination"/> with random bytes.</summary>
    void Fill(Span<byte> destination);
}
