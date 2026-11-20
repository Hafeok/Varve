// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Server;

/// <summary>
/// The system datasets of other fleets, as shipped beside the copies this
/// fleet serves (ADR 0185 decision 8): each under
/// <c>fleets/{fleet id}/</c> on this instance's store, followed read-only.
/// It answers which fleet wrote a commit of a copy, so that the management
/// position the commit records is read in the right system dataset.
/// </summary>
/// <remarks>
/// <para>
/// **Never for authorisation.** Nothing here is asked what a caller may do.
/// A grant in a shipped system dataset grants nothing in this fleet.
/// </para>
/// <para>
/// **What it costs.** A dataset's lineage record is read once, the first
/// time a commit of it is asked about, and again only when a replica has
/// been promoted since. A dataset that is no copy pays that one read.
/// </para>
/// </remarks>
[Contract(typeof(PromotionAcrossCopies.AManagementPositionBelongsToItsFleet), Role = "the source fleets' system datasets this instance follows")]
internal sealed class ShippedFleets(ObjectStores objects, OpenDatasets datasets) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, LineageRecord?> _lineages = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, (IAsyncDisposable Storage, Dataset System)?> _systems = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The fleet that wrote the commit of <paramref name="dataset"/> at <paramref name="position"/>, or null for one this fleet wrote.</summary>
    internal async ValueTask<ForeignFleet?> OfAsync(DatasetName dataset, Position position, CancellationToken cancellationToken)
    {
        ReplicaState state = datasets.ReplicaOf(dataset);

        if (!_lineages.TryGetValue(dataset.Value, out LineageRecord? lineage)
            || (lineage is { Promoted: null } && state == ReplicaState.None))
        {
            // Not read yet, or read as a replica that has been promoted since.
            lineage = await objects.LineageAsync(dataset.Value, cancellationToken).ConfigureAwait(false);
            _lineages[dataset.Value] = lineage;
        }

        if (lineage is not { Understood: true, Source: { } source })
        {
            return null;
        }

        // Every commit of a replica, and every commit at or below a promoted copy's promoted position.
        if (lineage.Promoted is { } promoted && position.Value > promoted.Position.Value)
        {
            return null;
        }

        DatasetName theirs = new((source.Prefix?.Value ?? dataset.Value + "/").TrimEnd('/'));
        return new ForeignFleet(source.Fleet is { } fleet ? await SystemAsync(fleet, cancellationToken).ConfigureAwait(false) : null, theirs);
    }

    /// <summary>Forgets what was read of a dataset's lineage: after a promotion on this instance.</summary>
    internal void Forget(DatasetName dataset) => _lineages.TryRemove(dataset.Value, out _);

    // The shipped system dataset of one source fleet, opened once and followed; none where it is not on the store.
    private async ValueTask<Dataset?> SystemAsync(DatasetId fleet, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_systems.TryGetValue(fleet.Value, out (IAsyncDisposable Storage, Dataset System)? known) && known is { } open)
            {
                return open.System;
            }

            try
            {
                if (await objects.OpenShippedFleetAsync(fleet, cancellationToken).ConfigureAwait(false) is not { } storage)
                {
                    // Not shipped yet, or never: asked again next time, since a shipper may still write it.
                    return null;
                }

                Dataset system = await Dataset.OpenFollowerAsync(storage, new DatasetOptions { Clock = objects.Clock }, cancellationToken).ConfigureAwait(false);
                _systems[fleet.Value] = (storage, system);
                return system;
            }
            catch (Exception error) when (error is IOException or System.Net.Http.HttpRequestException or TimeoutException or InvalidOperationException or LogVerificationException)
            {
                // What the commit's position meant cannot be said now. It is asked again next time.
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach ((IAsyncDisposable Storage, Dataset System)? held in _systems.Values)
        {
            if (held is { } open)
            {
                await open.System.DisposeAsync().ConfigureAwait(false);
                await open.Storage.DisposeAsync().ConfigureAwait(false);
            }
        }

        _systems.Clear();
        _gate.Dispose();
    }
}
