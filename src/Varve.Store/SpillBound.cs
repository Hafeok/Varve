// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store;

/// <summary>Which measure found a writer's memory unbounded while its spills fail (ADR 0179 point 6).</summary>
public enum SpillBound
{
    /// <summary>Not reached.</summary>
    None = 0,

    /// <summary>The memtable holds more than <see cref="Dataset.SpillBoundFactor"/> times its limit.</summary>
    Memtable = 1,

    /// <summary>The process's memory load reached the GC's high-load threshold.</summary>
    MemoryLoad = 2,
}
