// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Varve.Store;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// The two layers of ADR 0112, tested in process: an <see cref="ActivityListener"/>
/// on the source <c>Varve.Protocol</c> sees one span per request, named by
/// its operation and carrying the database semantic-convention attributes;
/// a <see cref="MeterListener"/> on <c>Varve.Store</c> sees every instrument
/// with the dataset as its dimension. Neither needs a package.
/// </summary>
[Collection(nameof(TelemetryTests))]
public class TelemetryTests
{
    [Fact]
    public async Task a_query_is_one_span_named_by_its_operation_with_the_conventions_attributes()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        using Spans spans = new();

        await host.Client.SendAsync(P.Update("datasets/d/sparql", "INSERT DATA { <http://ex/s> <http://ex/p> 1, 2, 3 }"), P.Ct);
        using HttpResponseMessage response = await host.Client.SendAsync(P.Query("datasets/d/sparql", "SELECT ?o { ?s ?p ?o }"), P.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Activity update = spans.Single("UPDATE");
        Activity select = spans.Single("SELECT");
        Assert.Equal("varve", select.GetTagItem("db.system.name"));
        Assert.Equal("d", select.GetTagItem("db.namespace"));
        Assert.Equal("SELECT", select.GetTagItem("db.operation.name"));
        Assert.Equal(response.Headers.GetValues("Varve-Request-Id").Single(), select.GetTagItem("varve.request_id"));
        Assert.Equal(1L, select.GetTagItem("varve.position"));
        Assert.Equal(3L, select.GetTagItem("db.response.returned_rows"));
        Assert.Equal(200, select.GetTagItem("http.response.status_code"));
        Assert.Null(select.GetTagItem("db.query.text"));
        Assert.Equal(1L, update.GetTagItem("varve.position"));
        Assert.Null(update.GetTagItem("db.query.text"));
    }

    [Fact]
    public async Task the_query_text_is_recorded_only_when_the_host_opts_in()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, telemetry: new TelemetryOptions { QueryText = true }, stopping: P.Ct);
        using Spans spans = new();

        await host.Client.SendAsync(P.Update("datasets/d/sparql", "INSERT DATA { <http://ex/s> <http://ex/p> 1 }"), P.Ct);
        await host.Client.SendAsync(P.Query("datasets/d/sparql", "ASK { ?s ?p ?o }"), P.Ct);

        Assert.Equal("INSERT DATA { <http://ex/s> <http://ex/p> 1 }", spans.Single("UPDATE").GetTagItem("db.query.text"));
        Assert.Equal("ASK { ?s ?p ?o }", spans.Single("ASK").GetTagItem("db.query.text"));
    }

    [Fact]
    public async Task every_endpoint_names_its_operation_and_a_selector_is_recorded_as_sent()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        using Spans spans = new();

        using HttpRequestMessage put = new(HttpMethod.Put, "datasets/d/graphs?graph=http://ex/g") { Content = new StringContent("<http://ex/s> <http://ex/p> <http://ex/o> .", System.Text.Encoding.UTF8, "text/turtle") };
        await host.Client.SendAsync(put, P.Ct);
        await host.Client.GetAsync("datasets/d/graphs?graph=http://ex/g", P.Ct);
        await host.Client.SendAsync(P.Query("datasets/d/sparql", "CONSTRUCT WHERE { ?s ?p ?o }", "text/turtle", asOf: "position:1"), P.Ct);
        await host.Client.GetAsync("datasets/d/commits?from=1&to=1", P.Ct);
        await host.Client.GetAsync("datasets/d/commits/1", P.Ct);
        await host.Client.GetAsync("datasets/d/diff?from=0&to=1", P.Ct);
        await host.Client.GetAsync("datasets/d/status", P.Ct);
        await host.Client.GetAsync("datasets/d/", P.Ct);
        await host.Client.GetAsync("datasets/d/no-such", P.Ct);

        Assert.Equal(1L, spans.Single("PUT graph").GetTagItem("varve.position"));
        Assert.Equal(1L, spans.Single("GET graph").GetTagItem("varve.position"));
        Activity construct = spans.Single("CONSTRUCT");
        Assert.Equal("position:1", construct.GetTagItem("varve.as_of"));
        Assert.Equal(1L, construct.GetTagItem("varve.position"));
        Assert.Equal("d", spans.Single("commits").GetTagItem("db.namespace"));
        Assert.Equal(1L, spans.Single("commit").GetTagItem("varve.position"));
        Assert.Equal(1L, spans.Single("diff").GetTagItem("varve.position"));
        Assert.Equal(1L, spans.Single("status").GetTagItem("varve.position"));
        Assert.Equal("service description", spans.Single("service description").GetTagItem("db.operation.name"));
        // An unmapped path is the host's 404, not a span of ours.
        Assert.DoesNotContain(spans.All, span => span.GetTagItem("http.response.status_code") is 404);
    }

    [Fact]
    public async Task the_store_measures_per_commit_per_pin_and_on_observation_with_the_dataset_as_dimension()
    {
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new Store.Log.DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System, Name = "metered" }, P.Ct);
        using Metrics metrics = new();

        Store.Log.CommitResult committed = await dataset.CommitAsync(new Store.Log.CommitRequest().Assert(Rdf.RdfTerm.Iri("http://ex/s"u8), Rdf.RdfTerm.Iri("http://ex/p"u8), Rdf.RdfTerm.Iri("http://ex/o"u8)), P.Ct);
        Assert.Equal(Store.Log.CommitOutcome.Committed, committed.Outcome);

        (double duration, IReadOnlyDictionary<string, object?> tags) = metrics.Histogram("varve.store.commit.duration").Single();
        Assert.True(duration >= 0);
        Assert.Equal("metered", tags["db.namespace"]);
        Assert.Equal("committed", tags["varve.outcome"]);

        // The queue: one in, one out, per commit.
        Assert.Equal([1L, -1L], metrics.Counter("varve.store.sequencer.queue_depth").Select(m => m.Value));

        using (DatasetView view = dataset.Pin())
        {
            Assert.Equal([1L], metrics.Counter("varve.store.pinned_reads").Select(m => m.Value));
        }

        Assert.Equal([1L, -1L], metrics.Counter("varve.store.pinned_reads").Select(m => m.Value));
        Assert.All(metrics.Counter("varve.store.pinned_reads"), m => Assert.Equal("metered", m.Tags["db.namespace"]));

        await dataset.CheckpointAsync(new Store.Log.Position(1), P.Ct);
        Assert.Single(metrics.Histogram("varve.store.checkpoint.duration"));

        // The gauges, recorded after the commit and again after the checkpoint.
        Assert.Equal(0L, metrics.Gauge("varve.store.projection.lag", "metered"));
        Assert.True(metrics.Gauge("varve.store.log.bytes", "metered") > 0);
        Assert.True(metrics.Gauge("varve.store.derived.bytes", "metered") > 0);
    }

    /// <summary>Every span the source <c>Varve.Protocol</c> finished while this listened.</summary>
    internal sealed class Spans : IDisposable
    {
        private readonly ConcurrentQueue<Activity> _finished = new();
        private readonly ActivityListener _listener;

        internal Spans()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == TelemetryOptions.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _finished.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        internal IReadOnlyCollection<Activity> All => _finished;

        internal Activity Single(string name) => Assert.Single(_finished, span => span.DisplayName == name);

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>Every measurement the meter <c>Varve.Store</c> made while this listened.</summary>
    internal sealed class Metrics : IDisposable
    {
        private readonly ConcurrentQueue<(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags)> _doubles = new();
        private readonly ConcurrentQueue<(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags)> _longs = new();
        private readonly MeterListener _listener;

        internal Metrics()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == TelemetryOptions.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => _doubles.Enqueue((instrument.Name, value, TagsOf(tags))));
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => _longs.Enqueue((instrument.Name, value, TagsOf(tags))));
            _listener.Start();
        }

        internal IEnumerable<(double Value, IReadOnlyDictionary<string, object?> Tags)> Histogram(string name) =>
            _doubles.Where(m => m.Instrument == name).Select(m => (m.Value, m.Tags));

        internal IEnumerable<(long Value, IReadOnlyDictionary<string, object?> Tags)> Counter(string name) =>
            _longs.Where(m => m.Instrument == name).Select(m => (m.Value, m.Tags));

        internal long Gauge(string name, string dataset) =>
            _longs.Where(m => m.Instrument == name && Equals(m.Tags["db.namespace"], dataset)).Select(m => m.Value).Last();

        public void Dispose() => _listener.Dispose();

        private static Dictionary<string, object?> TagsOf(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Dictionary<string, object?> copy = new(StringComparer.Ordinal);

            foreach (KeyValuePair<string, object?> tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            return copy;
        }
    }
}

/// <summary>Listeners are process-wide, so these tests run alone.</summary>
[CollectionDefinition(nameof(TelemetryTests), DisableParallelization = true)]
public sealed class TelemetryTestsRunAlone;
