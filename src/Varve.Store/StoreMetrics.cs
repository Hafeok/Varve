// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Varve.Store;

/// <summary>
/// The store's instruments (ADR 0112): one <see cref="Meter"/> per process,
/// named <c>Varve.Store</c>, and one of these per dataset, holding its
/// <c>db.namespace</c> tag. Every measurement is synchronous and per commit,
/// per checkpoint or per maintenance step, the gauges included: an
/// observable callback would hold the dataset for the meter's lifetime,
/// which is the process's, and a dataset abandoned without disposal would
/// never be collected. The BCL alone: an instrument nobody listens to costs
/// a flag test, and nothing is measured per quad or per solution.
/// </summary>
internal sealed class StoreMetrics
{
    private static readonly Meter Meter = new("Varve.Store");
    private static readonly Histogram<double> CommitDuration = Meter.CreateHistogram<double>("varve.store.commit.duration", unit: "s", description: "How long a commit took, from entering the sequencer's queue to its outcome.");
    private static readonly Histogram<double> CheckpointDuration = Meter.CreateHistogram<double>("varve.store.checkpoint.duration", unit: "s", description: "How long a checkpoint took to write and read back.");
    private static readonly UpDownCounter<long> QueueDepth = Meter.CreateUpDownCounter<long>("varve.store.sequencer.queue_depth", unit: "{commit}", description: "Commits waiting for the sequencer.");
    private static readonly UpDownCounter<long> PinnedReads = Meter.CreateUpDownCounter<long>("varve.store.pinned_reads", unit: "{read}", description: "Views pinned and not yet released.");
    private static readonly Gauge<long> ProjectionLag = Meter.CreateGauge<long>("varve.store.projection.lag", unit: "{position}", description: "How many positions the default projection is behind the head, as of the last commit or maintenance step.");
    private static readonly Gauge<long> LogBytes = Meter.CreateGauge<long>("varve.store.log.bytes", unit: "By", description: "The bytes of every commit's records in the log, as of the last commit.");
    private static readonly Gauge<long> DerivedBytes = Meter.CreateGauge<long>("varve.store.derived.bytes", unit: "By", description: "The bytes of the runs and checkpoints the dataset holds open, as of the last commit, checkpoint or maintenance step.");

    private readonly KeyValuePair<string, object?> _tag;

    internal StoreMetrics(string name)
    {
        _tag = new KeyValuePair<string, object?>("db.namespace", name);
    }

    /// <summary>A commit entered the sequencer's queue, or left it.</summary>
    internal void Queued(int delta)
    {
        if (QueueDepth.Enabled)
        {
            QueueDepth.Add(delta, _tag);
        }
    }

    /// <summary>A commit was sequenced, however it came out.</summary>
    internal void Committed(TimeSpan elapsed, string outcome)
    {
        if (CommitDuration.Enabled)
        {
            CommitDuration.Record(elapsed.TotalSeconds, _tag, new KeyValuePair<string, object?>("varve.outcome", outcome));
        }
    }

    /// <summary>A checkpoint was written.</summary>
    internal void Checkpointed(TimeSpan elapsed)
    {
        if (CheckpointDuration.Enabled)
        {
            CheckpointDuration.Record(elapsed.TotalSeconds, _tag);
        }
    }

    /// <summary>The gauges, as of now: after a commit, a checkpoint or a maintenance step.</summary>
    internal void Observe(Dataset dataset)
    {
        if (ProjectionLag.Enabled)
        {
            ProjectionLag.Record(dataset.Head.Value - dataset.ProjectionPosition.Value, _tag);
        }

        if (LogBytes.Enabled)
        {
            LogBytes.Record(dataset.LogBytes, _tag);
        }

        if (DerivedBytes.Enabled)
        {
            DerivedBytes.Record(dataset.DerivedBytes, _tag);
        }
    }

    /// <summary>A view was pinned; the answer, called once, says it was released.</summary>
    internal Action Pinned(Action? release)
    {
        if (PinnedReads.Enabled)
        {
            PinnedReads.Add(1, _tag);
        }

        return () =>
        {
            release?.Invoke();

            if (PinnedReads.Enabled)
            {
                PinnedReads.Add(-1, _tag);
            }
        };
    }
}
