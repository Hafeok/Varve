// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.Log;

/// <summary>What a commit is (spec §1).</summary>
public enum CommitKind : byte
{
    /// <summary>A change to the graph. Its delta is never empty (I4).</summary>
    Data = 0,

    /// <summary>A key destroyed. Empty delta; nothing produces one before erasure mode exists.</summary>
    Erasure = 1,

    /// <summary>A change to the dataset's settings (ADR 0021). Empty delta.</summary>
    Settings = 2,
}
