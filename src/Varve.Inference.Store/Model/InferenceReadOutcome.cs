// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Inference.Store.Model;

/// <summary>What a read of a binding's closure at a position found (ADR 0195 points 5, 5a and 6).</summary>
public enum InferenceReadOutcome
{
    /// <summary>The rows are held: the position is at or above the floor and the held history, and reached.</summary>
    Held,

    /// <summary>The projection has not reached the position: wait, then <c>inference-behind</c>.</summary>
    Behind,

    /// <summary>Below the floor or the held history: the closure is computed from scratch, as of the position.</summary>
    FromScratch,

    /// <summary>The binding is stopped by its memory limit at or below the position: <c>inference-unavailable</c>.</summary>
    Unavailable,
}
