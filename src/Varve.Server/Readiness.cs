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

/// <summary>
/// What readiness knows of one dataset: its entry's state and, when open, its
/// head, its projection, whether it failed, and whether its storage no longer
/// holds the writer epoch (ADR 0136).
/// </summary>
internal readonly record struct DatasetHealth(string Name, DatasetState State, string? Reason, long Head, long Projection, bool Failed, bool Superseded = false, DatasetRole Role = DatasetRole.Writer, Varve.Store.ExtentRetention? Retention = null);

/// <summary>One dataset's line in the readiness answer.</summary>
internal readonly record struct DatasetReadiness(string Name, string State, string? Reason, long? Lag, DatasetRole Role = DatasetRole.Writer);

/// <summary>
/// The readiness decision (ADR 0113), pure, so that its property can be
/// stated over generated states: every dataset that should be open is open,
/// no default projection is failed, each is within the allowed lag of its
/// head, and the host is not draining. A closed dataset is reported and does
/// not count. With a management plane (ADR 0163 point 13), the instance is
/// also not ready when its system role is superseded, its bootstrap
/// operators differ from the recorded ones, or its management lag exceeds
/// the allowed one.
/// </summary>
internal static class Readiness
{
    /// <summary>
    /// What the store keeps for readers, per dataset that has reader records
    /// (ADR 0162): reported beside readiness, and never a reason to be unready.
    /// </summary>
    internal static void WriteRetention(System.Text.Json.Utf8JsonWriter json, IEnumerable<DatasetHealth> datasets)
    {
        bool any = false;

        foreach (DatasetHealth dataset in datasets)
        {
            if (dataset.Retention is not { } retention)
            {
                continue;
            }

            if (!any)
            {
                json.WriteStartObject("retention");
                any = true;
            }

            json.WriteStartObject(dataset.Name);
            json.WriteNumber("catalogVersion", retention.Version.Value);
            json.WriteNumber("sets", retention.Sets);
            json.WriteNumber("unnamedSets", retention.UnnamedSets);
            json.WriteNumber("retainedBytes", retention.RetainedBytes);
            json.WriteStartArray("readers");

            foreach (Varve.Store.ReaderRetention reader in retention.Readers)
            {
                json.WriteStartObject();
                json.WriteString("reader", reader.Announcement.Reader.Value);
                json.WriteNumber("oldest", reader.Announcement.Oldest.Value);
                json.WriteNumber("newest", reader.Announcement.Newest.Value);
                json.WriteNumber("maxDerived", reader.Announcement.MaxDerived.Value);
                json.WriteNumber("retainedBytes", reader.RetainedBytes);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        if (any)
        {
            json.WriteEndObject();
        }
    }

    internal static (bool Ready, List<DatasetReadiness> Datasets) Evaluate(IEnumerable<DatasetHealth> datasets, long allowedLag, bool draining) =>
        Evaluate(datasets, allowedLag, draining, null, 0);

    internal static (bool Ready, List<DatasetReadiness> Datasets) Evaluate(IEnumerable<DatasetHealth> datasets, long allowedLag, bool draining, ManagementStatus? management, long allowedManagementLag)
    {
        bool ready = !draining;

        if (management is { } status && (status.Role == ManagementRole.Superseded || status.Bootstrap == BootstrapState.Mismatch || status.Lag > allowedManagementLag))
        {
            ready = false;
        }
        List<DatasetReadiness> states = [];

        foreach (DatasetHealth dataset in datasets)
        {
            DatasetReadiness line;

            switch (dataset.State)
            {
                // Read-only and never claiming again on its own: a newer
                // writer holds the dataset (ADR 0136). Liveness stays true.
                case DatasetState.Open when dataset.Superseded:
                    line = new DatasetReadiness(dataset.Name, "superseded", "a newer writer holds the dataset's epoch; this instance serves reads only", null);
                    ready = false;
                    break;
                case DatasetState.Open when dataset.Failed:
                    line = new DatasetReadiness(dataset.Name, "failed", dataset.Reason ?? "the default projection failed", null);
                    ready = false;
                    break;
                case DatasetState.Open when dataset.Head - dataset.Projection > allowedLag:
                    line = new DatasetReadiness(dataset.Name, "behind", null, dataset.Head - dataset.Projection);
                    ready = false;
                    break;
                case DatasetState.Open:
                    line = new DatasetReadiness(dataset.Name, "ready", null, dataset.Head - dataset.Projection);
                    break;
                case DatasetState.Closed:
                    line = new DatasetReadiness(dataset.Name, "closed", null, null);
                    break;
                default:
                    line = new DatasetReadiness(dataset.Name, "failed", dataset.Reason, null);
                    ready = false;
                    break;
            }

            states.Add(line with { Role = dataset.Role });
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
