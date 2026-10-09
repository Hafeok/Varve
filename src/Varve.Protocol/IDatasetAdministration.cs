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
/// The write side of the host's dataset map (ADR 0106; ADR 0091, amended
/// 2026-10-08): the datasets it knows with their state, and creating,
/// opening, closing and deleting one by name. The host owns the directories
/// under its root; the protocol never touches a file.
/// </summary>
[Contract(typeof(TheAdminApi.AdminEndpoints), Role = "the host's creation, listing, opening, closing and deletion of datasets by name")]
public interface IDatasetAdministration
{
    /// <summary>Every dataset the host knows, open, closed or failed, in name order.</summary>
    IReadOnlyList<DatasetEntry> List();

    /// <summary>Creates and opens a dataset of the name; <see cref="AdminOutcome.Exists"/> when the name is in use in any state.</summary>
    ValueTask<AdminOutcome> CreateAsync(DatasetName name, DatasetStorage storage, CancellationToken cancellationToken);

    /// <summary>Opens a closed or failed dataset of the name; <see cref="AdminOutcome.Done"/> for one already open.</summary>
    ValueTask<AdminOutcome> OpenAsync(DatasetName name, CancellationToken cancellationToken);

    /// <summary>Drains and closes an open dataset, releasing its lease; <see cref="AdminOutcome.NotFound"/> for one that is not open.</summary>
    ValueTask<AdminOutcome> CloseAsync(DatasetName name, CancellationToken cancellationToken);

    /// <summary>Deletes a closed dataset and everything it holds; <see cref="AdminOutcome.Open"/> while it is open.</summary>
    ValueTask<AdminOutcome> DeleteAsync(DatasetName name, CancellationToken cancellationToken);
}
