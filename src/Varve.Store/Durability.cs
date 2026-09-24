// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store;

/// <summary>
/// What a storage backend promises once a flush returns. Declared by the
/// backend, never assumed by the store (ADR 0018).
/// </summary>
public enum Durability
{
    /// <summary>In memory only; lost when the process exits.</summary>
    None,

    /// <summary>Handed to a transactional store that reported success — an IndexedDB transaction.</summary>
    Committed,

    /// <summary>Written through to durable media — <c>fsync</c> or its equivalent.</summary>
    Synchronised,
}
