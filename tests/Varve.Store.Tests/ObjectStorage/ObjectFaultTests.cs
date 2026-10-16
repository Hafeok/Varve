// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>What an injected fault does to the one request it hits.</summary>
internal enum ObjectFault
{
    /// <summary>The store refuses it before applying it: a 500, a 503, a refused connection.</summary>
    FailBefore,

    /// <summary>The store applies it and the answer is lost: a timeout, a dropped connection after the write.</summary>
    LoseAnswer,

    /// <summary>The process dies before the request reaches the store.</summary>
    KillBefore,

    /// <summary>The process dies after the store applied the request, before it hears back.</summary>
    KillAfter,

    /// <summary>Over HTTP: the store answers 500 without applying the request.</summary>
    Http500,

    /// <summary>Over HTTP: the store answers 503 with Retry-After, without applying the request.</summary>
    Http503,

    /// <summary>Over HTTP: the store answers 429 with Retry-After, without applying the request.</summary>
    Http429,

    /// <summary>Over HTTP: the request times out before it reaches the store.</summary>
    HttpTimeout,

    /// <summary>Over HTTP: the store applies the request and the response is lost.</summary>
    HttpLostResponse,
}

/// <summary>
/// Where one fault fires: counts requests while armed, and says what to do
/// with the one the fault is at. Shared by the in-memory store's hook and the
/// HTTP handler, so one workload runs over both.
/// </summary>
internal sealed class FaultController((int At, ObjectFault Kind)? fault)
{
    private int _requests;

    public bool Armed { get; set; }

    public bool Dead { get; set; }

    public bool Fired { get; private set; }

    public int Requests => _requests;

    /// <summary>The fault for this request, or null; throws when the process is dead.</summary>
    public ObjectFault? Next()
    {
        if (Dead)
        {
            throw new ProcessKilled();
        }

        if (!Armed)
        {
            return null;
        }

        int request = System.Threading.Interlocked.Increment(ref _requests);

        if (fault is not { } at || at.At != request)
        {
            return null;
        }

        Fired = true;
        return at.Kind;
    }
}

/// <summary>The process died: every request from it fails until a new one starts.</summary>
internal sealed class ProcessKilled : IOException
{
    public ProcessKilled()
        : base("injected: the process was killed")
    {
    }

    public ProcessKilled(string message)
        : base(message)
    {
    }

    public ProcessKilled(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// One run of the failure-injection workload over the object-storage backend
/// (#92's gate): a seeded sequence of commits, maintenance (which
/// consolidates), takeovers by a second writer, and graceful restarts, with
/// one fault injected at one request, or none.
/// </summary>
/// <remarks>
/// After a fault surfaces, the run does what a crashed host does: it
/// abandons the process and starts another, on a fresh cache directory, and
/// checks that the dataset reopens at the last closed commit with every
/// acknowledged commit in it, at most the one in doubt besides, and none
/// twice. At the end, faults off, every pinned and as-of read equals the
/// reference model, and the log continues.
/// </remarks>
internal sealed class ObjectFaultRun : IAsyncDisposable
{
    private readonly Random _random;
    private readonly int _steps;
    private readonly FaultController _faults;
    private readonly List<HashSet<string>> _history = [[]];
    private readonly List<string> _caches = [];
    private int _process;
    private Backend? _storage;
    private Dataset? _dataset;

    /// <summary>A run over the in-memory store, faults injected by its hook.</summary>
    public ObjectFaultRun(int seed, int steps, (int At, ObjectFault Kind)? fault)
        : this(seed, steps, new FaultController(fault), new MemoryObjectStore())
    {
        MemoryObjectStore memory = (MemoryObjectStore)Store;
        memory.Hook = (_, _) => MemoryHook(_faults.Next());
    }

    /// <summary>A run over any store whose requests consult <paramref name="faults"/> themselves.</summary>
    public ObjectFaultRun(int seed, int steps, FaultController faults, IObjectStore store)
    {
        _random = new Random(seed);
        _steps = steps;
        _faults = faults;
        Store = store;
    }

    public IObjectStore Store { get; }

    /// <summary>How many requests the workload made with faults armed.</summary>
    public int Requests => _faults.Requests;

    /// <summary>Whether the injected fault fired.</summary>
    public bool Fired => _faults.Fired;

    /// <summary>How many times a fault surfaced and a new process had to start.</summary>
    public int Crashes { get; private set; }

    /// <summary>How many takeovers fenced the writer before them.</summary>
    public int Fences { get; private set; }

    private int Acknowledged => _history.Count - 1;

    public async Task RunAsync()
    {
        _faults.Armed = true;
        await RestartAsync(inDoubt: null);

        for (int step = 0; step < _steps; step++)
        {
            int roll = _random.Next(100);

            if (roll < 70)
            {
                await CommitAsync();
            }
            else if (roll < 80)
            {
                await GuardAsync(async () => await _dataset!.MaintainAsync(T.Ct));
            }
            else if (roll < 90)
            {
                await TakeOverAsync();
            }
            else
            {
                await GracefulRestartAsync();
            }
        }

        _faults.Armed = false;
        await CrashAsync();
        await RestartAsync(inDoubt: null);
        await VerifyEveryPositionAsync();

        CommitResult next = await _dataset!.CommitAsync(new CommitRequest().Assert(T.Iri("final"), T.Iri("p"), T.Iri("o")), T.Ct);
        Assert.Equal(CommitOutcome.Committed, next.Outcome);
        _history.Add([.. _history[^1], "final p o"]);
        await CrashAsync();
        await RestartAsync(inDoubt: null);
    }

    public async ValueTask DisposeAsync()
    {
        _faults.Armed = false;
        await CrashAsync();

        foreach (string cache in _caches)
        {
            try
            {
                Directory.Delete(cache, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private Action? MemoryHook(ObjectFault? fault)
    {
        switch (fault)
        {
            case null:
                return null;
            case ObjectFault.FailBefore:
                throw new IOException("injected: the store refused the request");
            case ObjectFault.LoseAnswer:
                return () => throw new IOException("injected: the answer was lost");
            case ObjectFault.KillBefore:
                _faults.Dead = true;
                throw new ProcessKilled();
            case ObjectFault.KillAfter:
                return () =>
                {
                    _faults.Dead = true;
                    throw new ProcessKilled();
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, "Not a fault the in-memory store injects.");
        }
    }

    private async Task CommitAsync()
    {
        HashSet<string> state = _history[^1];
        HashSet<string> next = [.. state];
        CommitRequest request = new();

        if (state.Count > 0 && _random.Next(3) == 0)
        {
            string retracted = state.OrderBy(s => s, StringComparer.Ordinal).ElementAt(_random.Next(state.Count));
            string[] parts = retracted.Split(' ');
            request.Retract(T.Iri(parts[0]), T.Iri(parts[1]), T.Iri(parts[2]));
            next.Remove(retracted);
        }
        else
        {
            // Long enough that segments of 2 KiB roll and get consolidated.
            string subject = "s" + _random.Next(1_000_000).ToString(CultureInfo.InvariantCulture) + new string('x', 40);
            string quad = subject + " p o" + _random.Next(4).ToString(CultureInfo.InvariantCulture);

            if (state.Contains(quad))
            {
                return;
            }

            string[] parts = quad.Split(' ');
            request.Assert(T.Iri(parts[0]), T.Iri(parts[1]), T.Iri(parts[2]));
            next.Add(quad);
        }

        CommitResult result;

        try
        {
            result = await _dataset!.CommitAsync(request, T.Ct);
        }
        catch (Exception error) when (error is IOException or WriterSupersededException or InvalidOperationException)
        {
            await CrashAsync();
            await RestartAsync(inDoubt: next);
            return;
        }

        if (result.Outcome != CommitOutcome.Committed)
        {
            await CrashAsync();
            await RestartAsync(inDoubt: next);
            return;
        }

        Assert.Equal(new Position(Acknowledged + 1), result.Position);
        _history.Add(next);
    }

    // A second process claims the epoch while the first still runs: the
    // first's next commit must be refused, and nothing it does after the
    // fence may land in the log.
    private async Task TakeOverAsync()
    {
        Backend? taker = null;

        try
        {
            taker = await Backend.OpenAsync(Options("taker"), T.Ct);
        }
        catch (Exception error) when (error is IOException or LogVerificationException)
        {
            if (taker is not null)
            {
                await taker.DisposeAsync();
            }

            await CrashAsync();
            await RestartAsync(inDoubt: null);
            return;
        }

        if (taker.IsSuperseded)
        {
            // Lost to nothing but a fault; the old writer is still the writer.
            await taker.DisposeAsync();
            return;
        }

        string probe = "fenced p o" + _random.Next(4).ToString(CultureInfo.InvariantCulture);

        try
        {
            CommitResult refused = await _dataset!.CommitAsync(new CommitRequest().Assert(T.Iri("fenced"), T.Iri("p"), T.Iri(probe.Split(' ')[2])), T.Ct);
            Assert.NotEqual(CommitOutcome.Committed, refused.Outcome);
        }
        catch (WriterSupersededException)
        {
            Fences++;
        }
        catch (IOException)
        {
            // A fault surfaced on the old writer's write: it was not
            // acknowledged either way, and the fence still holds it.
        }

        // The old process ends as a crashed one would; the taker is the writer.
        bool dead = _faults.Dead;
        _faults.Dead = true;
        await DisposeCurrentAsync();
        _faults.Dead = dead;
        _storage = taker;

        try
        {
            _dataset = await T.OpenOrCreate(taker, T.Options(maxRecordBytes: 256, segmentBytes: 2048));
        }
        catch (Exception error) when (error is IOException or LogVerificationException or InvalidOperationException)
        {
            await CrashAsync();
            await RestartAsync(inDoubt: null);
            return;
        }

        Assert.Equal(new Position(Acknowledged), _dataset.Head);
    }

    private async Task GracefulRestartAsync()
    {
        await DisposeCurrentAsync();
        await RestartAsync(inDoubt: null);
    }

    private async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error) when (error is IOException or WriterSupersededException or LogVerificationException or InvalidOperationException)
        {
            await CrashAsync();
            await RestartAsync(inDoubt: null);
        }
    }

    // The process dies: nothing it sends from here on reaches the store, and
    // closing it releases nothing.
    private async Task CrashAsync()
    {
        _faults.Dead = true;
        await DisposeCurrentAsync();
        _faults.Dead = false;
    }

    private async Task DisposeCurrentAsync()
    {
        if (_dataset is not null)
        {
            try
            {
                await _dataset.DisposeAsync();
            }
            catch (Exception error) when (error is IOException or WriterSupersededException or InvalidOperationException or AggregateException)
            {
            }

            _dataset = null;
        }

        if (_storage is not null)
        {
            await _storage.DisposeAsync();
            _storage = null;
        }
    }

    // A new process: claims, adopts, fences, opens. A fault while it does is
    // another crash, and another start; the store must converge.
    private async Task RestartAsync(HashSet<string>? inDoubt)
    {
        for (int attempt = 0; ; attempt++)
        {
            Assert.True(attempt < 10, "the dataset never reopened");

            try
            {
                _storage = await Backend.OpenAsync(Options("process-" + ++_process), T.Ct);
                Assert.False(_storage.IsSuperseded, "a fresh process lost its claim with no other claimant");
                _dataset = await T.OpenOrCreate(_storage, T.Options(maxRecordBytes: 256, segmentBytes: 2048));
                break;
            }
            catch (Exception error) when (error is IOException or LogVerificationException or InvalidOperationException)
            {
                Crashes++;
                await CrashAsync();
            }
        }

        long head = _dataset.Head.Value;

        if (inDoubt is not null && head == Acknowledged + 1)
        {
            // The commit in doubt reached the store before its answer was lost.
            _history.Add(inDoubt);
        }

        Assert.True(head >= Acknowledged, "an acknowledged commit was lost: the head is " + head + ", " + Acknowledged + " were acknowledged");
        Assert.Equal(Acknowledged, head);

        using DatasetView view = _dataset.Pin();
        Assert.Equal(Expected(_history[(int)head]), T.Terms(view));
    }

    private async Task VerifyEveryPositionAsync()
    {
        for (int position = 0; position <= Acknowledged; position++)
        {
            using DatasetView view = await _dataset!.AsOfAsync(new Position(position), T.Ct);
            Assert.Equal(Expected(_history[position]), T.Terms(view));
        }
    }

    private static SortedSet<string> Expected(HashSet<string> state) =>
        new(state.Select(q => string.Join(" ", q.Split(' ').Select(t => "<http://example.org/" + t + ">"))), StringComparer.Ordinal);

    private ObjectStorageOptions Options(string instance)
    {
        string cache = Directory.CreateTempSubdirectory("varve-faults-").FullName;
        _caches.Add(cache);
        return new ObjectStorageOptions
        {
            Store = Store,
            Cache = new CacheDirectory(cache),
            Instance = new WriterInstance(instance),
            Clock = new InstantClock(ManualClock.Epoch().Now),
            ProbeRandomness = TestRandomness.Instance,
        };
    }
}

/// <summary>
/// The failure-injection gate of #92 over the object-storage backend: one
/// fault of each kind at every request of seeded workloads.
/// </summary>
public sealed class ObjectFaultTests
{
    /// <summary>
    /// The seeds a run explores: 9201 by default, which the gate runs in
    /// every build; <c>VARVE_OBJECT_FAULT_SEEDS</c> names more, comma-separated,
    /// for the deep exploration, and <c>VARVE_OBJECT_FAULT_STEPS</c> longer
    /// workloads.
    /// </summary>
    public static TheoryData<int> Seeds() =>
        [.. (Environment.GetEnvironmentVariable("VARVE_OBJECT_FAULT_SEEDS") ?? "9201")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(seed => int.Parse(seed, CultureInfo.InvariantCulture))];

    private static int Steps => int.TryParse(Environment.GetEnvironmentVariable("VARVE_OBJECT_FAULT_STEPS"), NumberStyles.None, CultureInfo.InvariantCulture, out int steps) ? steps : 24;

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task after_a_fault_at_any_request_the_dataset_reopens_at_the_last_closed_commit_with_nothing_lost_or_doubled(int seed)
    {
        int requests;

        await using (ObjectFaultRun clean = new(seed, Steps, fault: null))
        {
            await clean.RunAsync();
            requests = clean.Requests;
            Assert.True(clean.Fences > 0, "the clean run of seed " + seed + " fenced no writer, so it tests no takeover");
            Assert.Contains(((MemoryObjectStore)clean.Store).Names(), n => n.EndsWith(".seg", StringComparison.Ordinal));
        }

        int points = 0;
        int crashes = 0;

        foreach (ObjectFault kind in new[] { ObjectFault.FailBefore, ObjectFault.LoseAnswer, ObjectFault.KillBefore, ObjectFault.KillAfter })
        {
            for (int at = 1; at <= requests; at++)
            {
                await using ObjectFaultRun run = new(seed, Steps, (at, kind));

                try
                {
                    await run.RunAsync();
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException("seed " + seed + ", " + kind + " at request " + at + " of " + requests + ": " + error.Message, error);
                }

                points++;
                crashes += run.Crashes;
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine("seed " + seed + ": " + requests + " requests, " + points + " injection points, " + crashes + " crashes while reopening");
    }
}
