// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Model;

namespace Varve.Protocol;

/// <summary>
/// The host's dataset map (ADR 0106; ADR 0091, amended 2026-10-08; ADR
/// 0163): the datasets it knows with their state and role, and opening and
/// closing one by name for a host with no management plane. A dataset is
/// created and deleted by declaration (ADR 0163), never through this. The
/// host owns the directories under its root; the protocol never touches a
/// file.
/// </summary>
[Contract(typeof(TheManagementPlane.AdminEndpoints), Role = "the host's creation, listing, opening, closing and deletion of datasets by name")]
public interface IDatasetAdministration
{
    /// <summary>Every dataset the host knows, open, closed or failed, in name order.</summary>
    IReadOnlyList<DatasetEntry> List();

    /// <summary>Opens a closed or failed dataset of the name; <see cref="AdminOutcome.Done"/> for one already open.</summary>
    ValueTask<AdminOutcome> OpenAsync(DatasetName name, CancellationToken cancellationToken);

    /// <summary>Drains and closes an open dataset, releasing its lease; <see cref="AdminOutcome.NotFound"/> for one that is not open.</summary>
    ValueTask<AdminOutcome> CloseAsync(DatasetName name, CancellationToken cancellationToken);
}
