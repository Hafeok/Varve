// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Varve.Protocol.Model;

namespace Varve.Server;

/// <summary>What readiness knows of one dataset: its entry's state and, when open, its head, its projection and whether it failed.</summary>
internal readonly record struct DatasetHealth(string Name, DatasetState State, string? Reason, long Head, long Projection, bool Failed);

/// <summary>One dataset's line in the readiness answer.</summary>
internal readonly record struct DatasetReadiness(string Name, string State, string? Reason, long? Lag);

/// <summary>
/// The readiness decision (ADR 0113), pure, so that its property can be
/// stated over generated states: every dataset that should be open is open,
/// no default projection is failed, each is within the allowed lag of its
/// head, and the host is not draining. A closed dataset is reported and does
/// not count.
/// </summary>
internal static class Readiness
{
    internal static (bool Ready, List<DatasetReadiness> Datasets) Evaluate(IEnumerable<DatasetHealth> datasets, long allowedLag, bool draining)
    {
        bool ready = !draining;
        List<DatasetReadiness> states = [];

        foreach (DatasetHealth dataset in datasets)
        {
            DatasetReadiness line;

            switch (dataset.State)
            {
                case DatasetState.Open when dataset.Failed:
                    line = new DatasetReadiness(dataset.Name, "failed", dataset.Reason ?? "the default projection failed", null);
                    ready = false;
                    break;
                case DatasetState.Open when dataset.Head - dataset.Projection > allowedLag:
                    line = new DatasetReadiness(dataset.Name, "behind", null, dataset.Head - dataset.Projection);
                    ready = false;
                    break;
                case DatasetState.Open:
                    line = new DatasetReadiness(dataset.Name, "ready", null, null);
                    break;
                case DatasetState.Closed:
                    line = new DatasetReadiness(dataset.Name, "closed", null, null);
                    break;
                default:
                    line = new DatasetReadiness(dataset.Name, "failed", dataset.Reason, null);
                    ready = false;
                    break;
            }

            states.Add(line);
        }

        return (ready, states);
    }
}

/// <summary>
/// Whether the host is draining (ADR 0113). Readiness turns false the
/// moment <c>ApplicationStopping</c> fires; and <see cref="StoppingAsync"/>,
/// which the host calls on every lifecycle service before any service's
/// stop, so before the listener closes, waits the configured delay, so a
/// load balancer that probes readiness stops routing before requests are
/// refused.
/// </summary>
internal sealed class Drain(TimeSpan stopDelay, IHostApplicationLifetime lifetime) : IHostedLifecycleService
{
    private int _draining;

    internal bool Draining => Volatile.Read(ref _draining) == 1;

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStopping.Register(() => Volatile.Write(ref _draining, 1));
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _draining, 1);
        return stopDelay > TimeSpan.Zero ? Task.Delay(stopDelay, cancellationToken) : Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
