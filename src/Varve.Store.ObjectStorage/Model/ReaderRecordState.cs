// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.ObjectStorage.Model;

/// <summary>A reader record's state (ADR 0160).</summary>
public enum ReaderRecordState
{
    /// <summary>It announces the versions its reader holds.</summary>
    Announced,

    /// <summary>An operator forgot it: it holds nothing, and is about to be deleted.</summary>
    Forgotten,
}
