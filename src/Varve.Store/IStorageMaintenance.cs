// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store;

/// <summary>
/// Work a storage backend has to do of its own, off the commit path: the
/// object-storage backend's consolidation of a sealed segment's chunks into
/// one object (ADR 0135).
/// </summary>
/// <remarks>
/// The dataset calls it from its maintenance, which runs off the sequencer
/// under <see cref="DatasetOptions.Maintenance"/> (ADR 0070): in the background
/// when that is on, and from <see cref="Dataset.MaintainAsync"/> when the host
/// drives it. A backend never starts work of its own (ADR 0042). It is called
/// while appends continue, and touches nothing an append can.
/// </remarks>
[Contract(typeof(TheLogAsChunkObjects.StorageMaintenanceIsCalledByTheDataset), Role = "a backend's own maintenance, called by the dataset off the commit path")]
public interface IStorageMaintenance
{
    /// <summary>Whether the backend has work to do now.</summary>
    bool MaintenanceDue { get; }

    /// <summary>Does one step of the backend's work. True when it did something.</summary>
    ValueTask<bool> MaintainStorageAsync(CancellationToken cancellationToken);
}
