// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Results;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// The 7a properties (<c>change-feed.md</c> §6, ADRs 0094, 0096): generated
/// histories of SPARQL Update requests, some at the same instant, sent over
/// HTTP to an in-process server, and checked against the store in process.
/// </summary>
public class PropertyTests
{
    /// <summary>The iterations each property runs; the 7a record states them.</summary>
    internal const int Iterations = 1_000;

    private const string Prefix = "PREFIX : <http://ex/>\n";

    private static readonly string[] Kinds = ["INSERT DATA", "DELETE DATA", "DELETE WHERE", "INSERT {", "CLEAR", "DROP"];

    // ---- The generator: requests over a small vocabulary, so they collide.

    private static readonly Gen<string> Node = Gen.OneOfConst(":s0", ":s1", ":s2");
    private static readonly Gen<string> Pred = Gen.OneOfConst(":p0", ":p1");
    private static readonly Gen<string> Obj = Gen.OneOfConst(":s0", ":o0", "\"x\"", "1", "\"y\"@en");
    private static readonly Gen<string> InGraph = Gen.OneOfConst("", "", ":g0", ":g1");

    private static readonly Gen<string> Triple = Gen.Select(Gen.OneOf(Node, Gen.Const("_:b0")), Pred, Obj, InGraph,
        (s, p, o, g) => g.Length == 0 ? s + " " + p + " " + o + " ." : "GRAPH " + g + " { " + s + " " + p + " " + o + " }");

    private static readonly Gen<string> Operation = Gen.OneOf(
        Triple.List[1, 3].Select(t => "INSERT DATA { " + string.Join(" ", t) + " }"),
        Triple.List[1, 3].Select(t => "INSERT DATA { " + string.Join(" ", t) + " }"),
        Triple.Where(t => !t.Contains("_:", StringComparison.Ordinal)).List[1, 2].Select(t => "DELETE DATA { " + string.Join(" ", t) + " }"),
        Pred.Select(p => "DELETE WHERE { ?s " + p + " ?o }"),
        Gen.Select(Pred, Pred, (a, b) => "INSERT { ?s " + b + " _:n } WHERE { ?s " + a + " ?o }"),
        Gen.OneOfConst("CLEAR DEFAULT", "CLEAR GRAPH :g0", "DROP SILENT GRAPH :g1"));

    /// <summary>A request and the seconds the clock moves before it: 0 repeats the last instant.</summary>
    /// <remarks>
    /// A blank node label may not be shared by two operations of one request
    /// (SPARQL 1.1 Update §19.6), so each operation's labels get its index.
    /// </remarks>
    private static readonly Gen<(string Text, int Seconds)[]> History =
        Gen.Select(
            Operation.List[1, 3].Select(o => Prefix + string.Join(" ;\n", o.Select((op, i) =>
                op.Replace("_:b0", "_:b" + i.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                  .Replace("_:n", "_:n" + i.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)))),
            Gen.Int[0, 2]).Array[1, 8];

    // ---- The properties.

    [Fact]
    public async Task updates_over_http_equal_updates_in_process_commit_for_commit()
    {
        await using Harness harness = await Harness.StartAsync();
        Dictionary<string, int> kinds = [];

        await History.SampleAsync(async history =>
        {
            lock (kinds)
            {
                foreach ((string text, _) in history)
                {
                    foreach (string kind in Kinds)
                    {
                        kinds[kind] = kinds.GetValueOrDefault(kind) + (text.Contains(kind, StringComparison.Ordinal) ? 1 : 0);
                    }
                }
            }

            (Dataset viaHttp, string name, ManualClock clock) = await harness.AddAsync();
            ManualClock directClock = ManualClock.Epoch();
            await using Dataset direct = await P.NewDatasetAsync(directClock);

            foreach ((string text, int seconds) in history)
            {
                clock.Now = clock.Now.AddSeconds(seconds);
                directClock.Now = clock.Now;
                HttpResponseMessage response = await harness.Client.SendAsync(P.Update("datasets/" + name + "/sparql", text), P.Ct);
                CommitResult expected = await SparqlUpdate.ExecuteAsync(direct, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(text)), new UpdateOptions(), P.Ct);

                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                Assert.Equal(expected.Position.Value.ToString(CultureInfo.InvariantCulture), P.Header(response, "Varve-Position"));
            }

            Assert.Equal(direct.Head, viaHttp.Head);

            for (long p = 1; p <= direct.Head.Value; p++)
            {
                Assert.Equal(await DiffLinesAsync(direct, p), await DiffLinesAsync(viaHttp, p));
            }

            await viaHttp.DisposeAsync();
        }, iter: Iterations);

        // Every kind of operation occurred (ADR 0043's rule for generators).
        Assert.All(kinds, kind => Assert.True(kind.Value > 0, kind.Key + " never occurred"));
    }

    /// <summary>
    /// What <c>immutable</c> promises (ADR 0119): two as-of reads at the same
    /// closed position answer byte-identical bodies and the same validators,
    /// whatever was committed between them.
    /// </summary>
    [Fact]
    public async Task two_as_of_reads_at_one_closed_position_are_byte_identical_with_the_same_validators()
    {
        await using Harness harness = await Harness.StartAsync();

        await Gen.Select(History, Gen.Int[1, 8]).SampleAsync(async pair =>
        {
            ((string Text, int Seconds)[] history, int pick) = pair;
            (Dataset dataset, string name, ManualClock clock) = await harness.RunAsync(history);

            if (dataset.Head.Value == 0)
            {
                // A history that changed nothing has no closed position to read.
                await dataset.DisposeAsync();
                return;
            }

            long position = 1 + (pick % (int)dataset.Head.Value);
            string asOf = "position:" + position.ToString(CultureInfo.InvariantCulture);

            (byte[] firstBody, string firstValidators) = await ReadAsync(harness, name, asOf);
            clock.Now = clock.Now.AddSeconds(5);
            await harness.Client.SendAsync(P.Update("datasets/" + name + "/sparql", Prefix + "INSERT DATA { :later :p :o }"), P.Ct);
            (byte[] secondBody, string secondValidators) = await ReadAsync(harness, name, asOf);

            Assert.Equal(firstBody, secondBody);
            Assert.Equal(firstValidators, secondValidators);
            await dataset.DisposeAsync();
        }, iter: 200);

        static async Task<(byte[] Body, string Validators)> ReadAsync(Harness harness, string name, string asOf)
        {
            HttpResponseMessage response = await harness.Client.SendAsync(P.Query("datasets/" + name + "/sparql", "SELECT * WHERE { { ?s ?p ?o } UNION { GRAPH ?g { ?s ?p ?o } } } ORDER BY ?g ?s ?p ?o", asOf: asOf), P.Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string validators = response.Headers.ETag?.Tag + "|" + response.Content.Headers.LastModified?.ToString("o", CultureInfo.InvariantCulture) + "|" + response.Headers.NonValidated["Cache-Control"].ToString() + "|" + P.Header(response, "Varve-Position");
            Assert.Equal("private, max-age=31536000, immutable", response.Headers.NonValidated["Cache-Control"].ToString());
            return (await response.Content.ReadAsByteArrayAsync(P.Ct), validators);
        }
    }

    [Fact]
    public async Task a_feed_from_zero_replays_to_the_as_of_state_at_the_head()
    {
        await using Harness harness = await Harness.StartAsync();

        await History.SampleAsync(async history =>
        {
            (Dataset dataset, string name, _) = await harness.RunAsync(history);
            List<FeedRecord> feed = await harness.FeedAsync(name, "from=0&to=" + dataset.Head.Value.ToString(CultureInfo.InvariantCulture));
            SortedSet<string> replayed = new(StringComparer.Ordinal);
            P.Apply(replayed, feed);

            using DatasetView head = await dataset.AsOfAsync(dataset.Head, P.Ct);
            Assert.Equal(P.Lines(head), replayed);
            await dataset.DisposeAsync();
        }, iter: Iterations);
    }

    [Fact]
    public async Task a_feed_resumed_anywhere_delivers_exactly_the_commits_after_it()
    {
        await using Harness harness = await Harness.StartAsync();

        await Gen.Select(History, Gen.Int[0, 100]).SampleAsync(async sample =>
        {
            (Dataset dataset, string name, _) = await harness.RunAsync(sample.Item1);
            long head = dataset.Head.Value;
            long from = head == 0 ? 0 : sample.Item2 % (head + 1);
            string to = "&to=" + head.ToString(CultureInfo.InvariantCulture);

            List<FeedRecord> all = await harness.FeedAsync(name, "from=0" + to);
            List<FeedRecord> resumed = await harness.FeedAsync(name, "from=" + from.ToString(CultureInfo.InvariantCulture) + to);
            Assert.Equal(Render(all.Where(r => r.Position.Value > from)), Render(resumed));
            await dataset.DisposeAsync();
        }, iter: Iterations);
    }

    [Fact]
    public async Task a_bounded_feed_then_the_rest_is_the_feed_from_its_start()
    {
        await using Harness harness = await Harness.StartAsync();

        await Gen.Select(History, Gen.Int[0, 100], Gen.Int[0, 100]).SampleAsync(async sample =>
        {
            (Dataset dataset, string name, _) = await harness.RunAsync(sample.Item1);
            long head = dataset.Head.Value;
            long a = sample.Item2 % (head + 1);
            long b = a + (sample.Item3 % (head - a + 1));
            static string Of(long x) => x.ToString(CultureInfo.InvariantCulture);

            List<FeedRecord> first = await harness.FeedAsync(name, "from=" + Of(a) + "&to=" + Of(b));
            List<FeedRecord> rest = await harness.FeedAsync(name, "from=" + Of(b) + "&to=" + Of(head));
            List<FeedRecord> whole = await harness.FeedAsync(name, "from=" + Of(a) + "&to=" + Of(head));
            Assert.Equal(Render(whole), Render(first.Concat(rest)));
            await dataset.DisposeAsync();
        }, iter: Iterations);
    }

    [Fact]
    public async Task timestamps_resolve_on_the_feed_and_on_as_of_as_the_store_resolves_them()
    {
        await using Harness harness = await Harness.StartAsync();

        await Gen.Select(History, Gen.Int[0, 1000], Gen.Int[-1, 1]).SampleAsync(async sample =>
        {
            (Dataset dataset, string name, ManualClock clock) = await harness.RunAsync(sample.Item1);
            List<FeedRecord> commits = await harness.FeedAsync(name, "from=0&to=" + dataset.Head.Value.ToString(CultureInfo.InvariantCulture));
            List<DateTimeOffset> times = [.. commits.Select(c => c.Timestamp.Value)];

            // An instant at, just before or just after a commit's, or before the first.
            DateTimeOffset instant = times.Count == 0
                ? ManualClock.Epoch().Now
                : times[sample.Item2 % times.Count].AddTicks(sample.Item3);
            string text = Instants.Format(new CommitTimestamp(instant));

            // I5, in process: the greatest position at or before the instant.
            long atOrBefore = dataset.PositionAt(new CommitTimestamp(instant)).Value;
            Assert.Equal(commits.Count(c => c.Timestamp.Value <= instant), atOrBefore);

            HttpResponseMessage asOf = await harness.Client.SendAsync(P.Query("datasets/" + name + "/sparql", "ASK {}", asOf: "time:" + text), P.Ct);
            if (atOrBefore == 0)
            {
                Assert.Equal(HttpStatusCode.NotFound, asOf.StatusCode);
            }
            else
            {
                Assert.Equal(atOrBefore.ToString(CultureInfo.InvariantCulture), P.Header(asOf, "Varve-Position"));
            }

            // The feed: a start at the earliest commit at or after, an end at the latest at or before.
            List<FeedRecord> started = await harness.FeedAsync(name, "fromTime=" + Uri.EscapeDataString(text) + "&to=" + dataset.Head.Value.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(commits.Where(c => c.Timestamp.Value >= instant).Select(c => c.Position), started.Select(c => c.Position));

            List<FeedRecord> ended = await harness.FeedAsync(name, "from=0&toTime=" + Uri.EscapeDataString(text));
            Assert.Equal(commits.Where(c => c.Timestamp.Value <= instant).Select(c => c.Position), ended.Select(c => c.Position));
            await dataset.DisposeAsync();
        }, iter: Iterations);
    }

    [Fact]
    public async Task a_stale_if_match_is_always_412_and_never_commits()
    {
        await using Harness harness = await Harness.StartAsync();

        await Gen.Select(History, Gen.Int[1, 50], Gen.Bool).SampleAsync(async sample =>
        {
            (Dataset dataset, string name, _) = await harness.RunAsync(sample.Item1);
            Position head = dataset.Head;
            long stale = (head.Value + sample.Item2) % (head.Value + 51);
            stale = stale == head.Value ? head.Value + 1 : stale;

            HttpRequestMessage write = sample.Item3
                ? P.Update("datasets/" + name + "/sparql", Prefix + "INSERT DATA { :new :p :o }", "\"" + stale.ToString(CultureInfo.InvariantCulture) + "\"")
                : new HttpRequestMessage(HttpMethod.Put, new Uri("datasets/" + name + "/graphs?default", UriKind.Relative))
                {
                    Content = new StringContent("<http://ex/new> <http://ex/p> <http://ex/o> .", Encoding.UTF8, "application/n-triples"),
                    Headers = { { "If-Match", "\"" + stale.ToString(CultureInfo.InvariantCulture) + "\"" } },
                };

            HttpResponseMessage response = await harness.Client.SendAsync(write, P.Ct);
            Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            Assert.Equal(head.Value.ToString(CultureInfo.InvariantCulture), P.Header(response, "Varve-Position"));
            Assert.Equal(head, dataset.Head);
            await dataset.DisposeAsync();
        }, iter: Iterations);
    }

    [Fact]
    public async Task an_as_of_read_over_http_equals_the_as_of_read_in_process()
    {
        await using Harness harness = await Harness.StartAsync();
        const string all = "SELECT ?s ?p ?o ?g { { ?s ?p ?o } UNION { GRAPH ?g { ?s ?p ?o } } }";

        await Gen.Select(History, Gen.Int[0, 100]).SampleAsync(async sample =>
        {
            (Dataset dataset, string name, _) = await harness.RunAsync(sample.Item1);
            long position = sample.Item2 % (dataset.Head.Value + 1);

            HttpResponseMessage response = await harness.Client.SendAsync(
                P.Query("datasets/" + name + "/sparql", all, "application/sparql-results+json", "position:" + position.ToString(CultureInfo.InvariantCulture)), P.Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            SortedSet<string> overHttp = Rows(await response.Content.ReadAsByteArrayAsync(P.Ct));

            using DatasetView view = await dataset.AsOfAsync(new Position(position), P.Ct);
            Assert.Equal(P.Lines(view), overHttp);
            await dataset.DisposeAsync();
        }, iter: Iterations);
    }

    [Fact]
    public async Task concurrent_writers_at_one_position_commit_exactly_once()
    {
        await using Harness harness = await Harness.StartAsync();

        await Gen.Select(History, Gen.Int[2, 6]).SampleAsync(async sample =>
        {
            (Dataset dataset, string name, _) = await harness.RunAsync(sample.Item1);
            Position head = dataset.Head;
            string expect = "\"" + head.Value.ToString(CultureInfo.InvariantCulture) + "\"";

            HttpResponseMessage[] answers = await Task.WhenAll(Enumerable.Range(0, sample.Item2).Select(i =>
                harness.Client.SendAsync(P.Update("datasets/" + name + "/sparql", Prefix + "INSERT DATA { :w" + i + " :p :o }", expect), P.Ct)));

            Assert.Equal(1, answers.Count(a => a.StatusCode == HttpStatusCode.NoContent));
            Assert.All(answers.Where(a => a.StatusCode != HttpStatusCode.NoContent),
                a => Assert.True(a.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed, a.StatusCode.ToString()));
            Assert.Equal(head.Value + 1, dataset.Head.Value);
            await dataset.DisposeAsync();
        }, iter: Iterations);
    }

    // ---- Support.

    private static async Task<SortedSet<string>> DiffLinesAsync(Dataset dataset, long position)
    {
        QuadDelta delta = await dataset.DiffAsync(new Position(position - 1), new Position(position), P.Ct);
        using DatasetView view = dataset.Pin();
        SortedSet<string> lines = new(StringComparer.Ordinal);

        foreach (Quad quad in delta.Asserted)
        {
            lines.Add("+ " + Line(view, quad));
        }

        foreach (Quad quad in delta.Retracted)
        {
            lines.Add("- " + Line(view, quad));
        }

        return lines;
    }

    private static string Line(DatasetView view, Quad quad)
    {
        RdfTerm Term(TermHandle handle) => view.TryExternalise(handle, out RdfTerm? term) ? term : throw new InvalidOperationException();
        return P.Line(Term(quad.Subject), Term(quad.Predicate), Term(quad.Object), quad.Graph.IsNone ? null : Term(quad.Graph));
    }

    private static List<string> Render(IEnumerable<FeedRecord> records) =>
        [.. records.Select(r => r.Position.Value.ToString(CultureInfo.InvariantCulture) + " " + r.CommitKind + " "
            + string.Join(" | ", r.Changes.Select(c => (c.Kind == FeedChangeKind.Assert ? "+" : "-") + P.Line(c))))];

    private static SortedSet<string> Rows(byte[] json)
    {
        SortedSet<string> rows = new(StringComparer.Ordinal);
        SparqlResultsReader reader = new(json, SparqlResultsFormat.Json);
        Assert.True(reader.ReadHead());

        while (reader.Read())
        {
            SolutionView row = reader.Current;
            Assert.True(row.TryGet(0, out RdfTermView s));
            Assert.True(row.TryGet(1, out RdfTermView p));
            Assert.True(row.TryGet(2, out RdfTermView o));
            RdfTerm? g = row.TryGet(3, out RdfTermView graph) ? graph.Materialise() : null;
            rows.Add(P.Line(s.Materialise(), p.Materialise(), o.Materialise(), g));
        }

        return rows;
    }

    /// <summary>One server for a property's iterations; each iteration adds a fresh dataset under a fresh name.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ProtocolTestHost.Datasets _datasets;
        private readonly ProtocolTestHost _host;
        private int _next;

        private Harness(ProtocolTestHost.Datasets datasets, ProtocolTestHost host)
        {
            _datasets = datasets;
            _host = host;
        }

        internal HttpClient Client => _host.Client;

        internal static async Task<Harness> StartAsync()
        {
            ProtocolTestHost.Datasets datasets = new();
            return new Harness(datasets, await ProtocolTestHost.StartAsync(datasets, stopping: P.Ct));
        }

        internal async Task<(Dataset Dataset, string Name, ManualClock Clock)> AddAsync()
        {
            ManualClock clock = ManualClock.Epoch();
            Dataset dataset = await P.NewDatasetAsync(clock);
            string name = "d" + Interlocked.Increment(ref _next).ToString(CultureInfo.InvariantCulture);
            _datasets.Add(name, dataset);
            return (dataset, name, clock);
        }

        /// <summary>A fresh dataset with the history applied over HTTP.</summary>
        internal async Task<(Dataset Dataset, string Name, ManualClock Clock)> RunAsync((string Text, int Seconds)[] history)
        {
            (Dataset dataset, string name, ManualClock clock) = await AddAsync();

            foreach ((string text, int seconds) in history)
            {
                clock.Now = clock.Now.AddSeconds(seconds);
                HttpResponseMessage response = await Client.SendAsync(P.Update("datasets/" + name + "/sparql", text), P.Ct);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            return (dataset, name, clock);
        }

        internal async Task<List<FeedRecord>> FeedAsync(string name, string query)
        {
            HttpResponseMessage response = await Client.SendAsync(P.Get("datasets/" + name + "/commits?" + query), P.Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await P.ReadFeedAsync(response);
        }

        public ValueTask DisposeAsync() => _host.DisposeAsync();
    }
}
