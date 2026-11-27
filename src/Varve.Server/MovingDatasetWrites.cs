// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Model;

namespace Varve.Server;

/// <summary>
/// Whether a write to a dataset advances the system follower once before its
/// grants are pinned: when the dataset's log records a later management
/// position than this instance applied (ADR 0165 point 5), and when the
/// dataset's event declarations are moving here, so that a cancel the system
/// writer has answered is in force for it (ADR 0167, amended for #254). When
/// that advance fails, the catalogue's state is unconfirmed, and a catalogue
/// write is refused <c>management-behind</c>.
/// </summary>
[Contract(typeof(EventDeclarationsMoveToTheManagementPlane.MovingWriteAdvancesFirst), Role = "the host's decision to advance before it authorises a write")]
internal static class MovingDatasetWrites
{
    /// <summary>Whether a write to <paramref name="dataset"/> advances <paramref name="fleet"/> first.</summary>
    internal static bool AdvanceFirst(Fleet fleet, DatasetName dataset) =>
        fleet.Recorded(dataset).Value > fleet.Current.Table.Position.Value || fleet.IsMoving(dataset);
}
