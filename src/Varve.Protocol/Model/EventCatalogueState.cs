// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Protocol.Model;

/// <summary>
/// Whether a dataset's catalogue graphs take writes, as the host that writes
/// the dataset knows from the management position it has applied (ADR 0167,
/// amended for #254).
/// </summary>
public enum EventCatalogueState
{
    /// <summary>The event declarations are in the dataset: catalogue writes are checked as declarations.</summary>
    Open,

    /// <summary>The declarations are moving or moved to the management plane: catalogue writes are refused <c>409 event-catalogue-moved</c>.</summary>
    Frozen,

    /// <summary>
    /// The declarations were moving at the last position applied, and the host
    /// could not read the management plane since, so a cancel may be in force
    /// that it has not seen: catalogue writes are refused
    /// <c>503 management-behind</c>, which a client sends again.
    /// </summary>
    Unconfirmed,
}
