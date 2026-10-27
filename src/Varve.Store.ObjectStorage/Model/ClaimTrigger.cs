// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.ObjectStorage.Model;

/// <summary>Why an epoch was claimed, as the writer record keeps it (ADR 0136).</summary>
public enum ClaimTrigger
{
    /// <summary>A process opened the dataset for write: a start, or a restart.</summary>
    Restart,

    /// <summary>An operator took the writer epoch over, naming the epoch they took it from.</summary>
    Operator,

    /// <summary>A warm standby claimed after the holder released the epoch (ADR 0179 point 6).</summary>
    Abdication,

    /// <summary>A warm standby claimed on a takeover request after the orchestrator's probe failed, naming the epoch (ADR 0179 point 6).</summary>
    Probe,
}
