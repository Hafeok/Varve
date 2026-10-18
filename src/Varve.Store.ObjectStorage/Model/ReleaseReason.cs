// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.ObjectStorage.Model;

/// <summary>Why a writer released its epoch, as the writer record keeps it (ADR 0136).</summary>
public enum ReleaseReason
{
    /// <summary>The writer closed gracefully.</summary>
    Shutdown,

    /// <summary>An operator took the epoch over and released it for the next writer.</summary>
    OperatorTakeover,
}
