// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Varve.Management;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.Tests.Objects;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// ADR 0244's idle collection runs only when the whole server is idle, the
/// system dataset included (point 6, as accepted on #264). So it depends on a
/// fleet with no requests never committing to its system dataset on a timer
/// (#332). Two instances of the server's own <see cref="Fleet"/>, a writer
/// and a follower, run their interval loops in one process over a store in
/// memory, under a clock the test steps: no request, and no real wait for
/// an interval.
/// </summary>
public sealed class IdleFleetTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    // Ten minutes of fleet time.
    private const int Ticks = 600;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task an_idle_fleets_system_dataset_does_not_move_and_the_idle_collection_runs_after_a_checkpoint()
    {
        SteppedClock clock = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        MemoryObjectStore store = new();
        string caches = Directory.CreateTempSubdirectory("varve-idle-fleet-").FullName;
        using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        try
        {
            await using Fleet writer = await StartAsync("a", store, clock, caches);

            // One dataset, placed on the writer, with one commit to checkpoint later.
            await RunningServer.DeclareAsync(writer, Trig("d", "a"));
            Assert.True(writer.Datasets.TryResolve(new DatasetName("d"), out Dataset? dataset));
            Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Quad(1), Ct)).Outcome);

            await using Fleet follower = await StartAsync("b", store, clock, caches);
            await follower.AdvanceAsync(Ct);
            Assert.Equal(writer.System.Head, follower.System.Head);

            // Both interval loops, as ServerHost runs them: each waits one interval on the clock.
            Task[] loops = [writer.RunAsync(stopping.Token), follower.RunAsync(stopping.Token)];
            await clock.WaitForAsync(Interval, 2, Ct);

            Position system = writer.System.Head;
            IReadOnlyList<Position> heads = writer.Heads();
            string[] log = SystemLog(store);

            for (int tick = 0; tick < Ticks; tick++)
            {
                clock.Advance(Interval);
                await clock.WaitForAsync(Interval, 2, Ct);
            }

            // The system dataset did not move, on either instance or in the store.
            Assert.Equal(system, writer.System.Head);
            Assert.Equal(system, follower.System.Head);
            Assert.Equal(log, SystemLog(store));
            Assert.Equal(heads, writer.Heads());

            // A checkpoint, then the idle wait on the same clock, ticking the
            // loops through it: the collection runs. Only the wait and the
            // collection are stood in for, as in ObjectStorageServerTests.
            await dataset.CheckpointAsync(dataset.Head, Ct);
            int released = 0;
            bool idle = await IdleRelease.WhenIdleAsync(
                () => dataset.MaintainAsync(Ct),
                writer.Heads,
                async () =>
                {
                    for (TimeSpan waited = TimeSpan.Zero; waited < IdleRelease.IdleFor; waited += Interval)
                    {
                        clock.Advance(Interval);
                        await clock.WaitForAsync(Interval, 2, Ct);
                    }
                },
                () => released++);

            Assert.True(idle, "The server was not idle through the wait after the checkpoint.");
            Assert.Equal(1, released);
            Assert.Equal(system, writer.System.Head);
            Assert.Equal(log, SystemLog(store));

            await stopping.CancelAsync();
            await Task.WhenAll(loops);
        }
        finally
        {
            await stopping.CancelAsync();
            Directory.Delete(caches, recursive: true);
        }
    }

    /// <summary>
    /// The interval loop, stepped, does advance what moves (the control): a
    /// commit to the system dataset by the writer reaches the follower on the
    /// next tick, so the loop the test above watches is running.
    /// </summary>
    [Fact]
    public async Task the_stepped_follower_takes_a_system_commit_on_the_next_tick()
    {
        SteppedClock clock = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        MemoryObjectStore store = new();
        string caches = Directory.CreateTempSubdirectory("varve-idle-fleet-").FullName;
        using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        try
        {
            await using Fleet writer = await StartAsync("a", store, clock, caches);
            await using Fleet follower = await StartAsync("b", store, clock, caches);
            await follower.AdvanceAsync(Ct);
            Task[] loops = [writer.RunAsync(stopping.Token), follower.RunAsync(stopping.Token)];
            await clock.WaitForAsync(Interval, 2, Ct);

            Position before = follower.System.Head;
            await RunningServer.DeclareAsync(writer, Trig("e", "a"));
            Assert.True(writer.System.Head > before);
            Assert.Equal(before, follower.System.Head);

            clock.Advance(Interval);
            await clock.WaitForAsync(Interval, 2, Ct);
            Assert.Equal(writer.System.Head, follower.System.Head);

            await stopping.CancelAsync();
            await Task.WhenAll(loops);
        }
        finally
        {
            await stopping.CancelAsync();
            Directory.Delete(caches, recursive: true);
        }
    }

    private static async Task<Fleet> StartAsync(string instance, MemoryObjectStore store, SteppedClock clock, string caches)
    {
        ServerSettings settings = new();
        settings.Auth.Mode = "Anonymous";
        settings.ObjectStorage.Provider = "Azure";
        settings.ObjectStorage.ContainerUrl = "https://fleet.invalid/idle";
        settings.ObjectStorage.CachePath = Path.Combine(caches, instance);
        settings.ObjectStorage.Instance = instance;
        settings.Fleet.Writer = "a";
        settings.Fleet.FollowInterval = Interval;
        return await Fleet.StartAsync(settings, ObjectStores.Over(store, settings.ObjectStorage, clock), recordBootstrap: false, clock, NullLogger.Instance, new HttpContextAccessor(), Ct);
    }

    // The system dataset's log as the store holds it: every object's name and length.
    private static string[] SystemLog(MemoryObjectStore store) =>
        [.. store.Names().Where(name => name.StartsWith(Fleet.SystemName + "/log/", StringComparison.Ordinal)).Select(name => name + " " + store.Get(name)!.Length)];

    private static string Trig(string dataset, string placedOn) =>
        "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
        + "<" + ManagementVocabulary.DatasetGraphPrefix + dataset + "> {\n"
        + "  <urn:idle:" + dataset + "> a varve:Dataset ; varve:datasetName \"" + dataset + "\" ; varve:storage varve:ObjectStorage ; varve:placedOn \"" + placedOn + "\" .\n"
        + "}\n";

    private static CommitRequestBuilder Quad(int number) =>
        new CommitRequestBuilder().Assert(
            Varve.Rdf.RdfTerm.Iri(Encoding.UTF8.GetBytes("https://example.org/s" + number.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            Varve.Rdf.RdfTerm.Iri("https://example.org/p"u8),
            Varve.Rdf.RdfTerm.Iri("https://example.org/o"u8));

    /// <summary>
    /// A clock that moves only when the test advances it, and fires the
    /// timers that fall due then. It counts the one-shot timers armed for a
    /// given due time, which is how the test knows an interval loop has
    /// finished its round and is waiting for the next.
    /// </summary>
    private sealed class SteppedClock(DateTimeOffset start) : TimeProvider
    {
        private readonly Lock _gate = new();
        private readonly List<SteppedTimer> _timers = [];
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => GetUtcNow().UtcTicks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            SteppedTimer timer = new(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        /// <summary>Moves the clock and runs every timer that falls due, in due order.</summary>
        public void Advance(TimeSpan by)
        {
            DateTimeOffset until;

            lock (_gate)
            {
                _now += by;
                until = _now;
            }

            while (true)
            {
                SteppedTimer? due;

                lock (_gate)
                {
                    due = _timers.Where(t => t.Due <= until).OrderBy(t => t.Due).FirstOrDefault();
                    due?.Fired();
                }

                if (due is null)
                {
                    return;
                }

                due.Run();
            }
        }

        /// <summary>Until <paramref name="count"/> timers of <paramref name="dueTime"/> are armed and not yet due.</summary>
        public async Task WaitForAsync(TimeSpan dueTime, int count, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

            while (Armed(dueTime) < count)
            {
                Assert.True(DateTime.UtcNow < deadline, "The interval loops did not come back to their wait.");
                await Task.Delay(TimeSpan.FromMilliseconds(1), TimeProvider.System, cancellationToken);
            }
        }

        private int Armed(TimeSpan dueTime)
        {
            lock (_gate)
            {
                return _timers.Count(t => t.Requested == dueTime);
            }
        }

        private sealed class SteppedTimer(SteppedClock clock, TimerCallback callback, object? state) : ITimer
        {
            private TimeSpan _period = Timeout.InfiniteTimeSpan;

            public DateTimeOffset Due { get; private set; }

            public TimeSpan Requested { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._gate)
                {
                    clock._timers.Remove(this);
                    _period = period;
                    Requested = dueTime;

                    if (dueTime != Timeout.InfiniteTimeSpan)
                    {
                        Due = clock._now + dueTime;
                        clock._timers.Add(this);
                    }
                }

                return true;
            }

            // Under the clock's lock: a one-shot timer is spent, a periodic one re-armed.
            public void Fired()
            {
                clock._timers.Remove(this);

                if (_period != Timeout.InfiniteTimeSpan && _period > TimeSpan.Zero)
                {
                    Due += _period;
                    Requested = _period;
                    clock._timers.Add(this);
                }
            }

            public void Run() => callback(state);

            public void Dispose()
            {
                lock (clock._gate)
                {
                    clock._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
