// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// The commit path, and the set of handles a delivered commit carries.
/// </summary>
/// <remarks>
/// <para>
/// <c>Commit</c> is one request of N quads committed into an empty memory
/// store, its terms built outside the time: what the sequencer does with a
/// request, from <c>CommitAsync</c> to the answer.
/// </para>
/// <para>
/// <c>FirstExternaliseOfACarriedHandle</c> is the one place a commit builds
/// the handles it carries (<c>Commit.TryExternalise</c> for a handle not among
/// its allocations): the second commit of N quads re-uses the first's terms,
/// is read fresh from the log for every invocation, and one of its subjects is
/// externalised, so the time is the set's construction over 4N handles and
/// one lookup.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(15)]
[InvocationCount(1, 1)]
public class CommitPathBenchmarks : IDisposable
{
    private StoreDatasetType? _dataset;
    private CommitRequestBuilder? _request;
    private StoreDatasetType? _carrying;
    private Commit? _second;
    private TermHandle _subject;

    [Params(10_000, 100_000)]
    public int Count { get; set; }

    [IterationSetup(Target = nameof(Commit))]
    public void EmptyStoreAndARequest()
    {
        _dataset = Empty();
        _request = Request(Count, "p");
    }

    [IterationCleanup(Target = nameof(Commit))]
    public void DisposeStore()
    {
        _dataset?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _dataset = null;
    }

    [Benchmark]
    public async Task<CommitOutcome> Commit() => (await _dataset!.CommitAsync(_request!)).Outcome;

    [GlobalSetup(Target = nameof(FirstExternaliseOfACarriedHandle))]
    public void TwoCommits()
    {
        _carrying = Empty();
        _carrying.CommitAsync(Request(Count, "p")).AsTask().GetAwaiter().GetResult();
        _carrying.CommitAsync(Request(Count, "q")).AsTask().GetAwaiter().GetResult();
    }

    [IterationSetup(Target = nameof(FirstExternaliseOfACarriedHandle))]
    public void ReadTheSecondCommit()
    {
        _second = Second(_carrying!).GetAwaiter().GetResult();
        _subject = _second.Delta.Asserted[Count / 2].Subject;
    }

    [Benchmark]
    public bool FirstExternaliseOfACarriedHandle() => _second!.TryExternalise(_subject, out _);

    [GlobalCleanup(Target = nameof(FirstExternaliseOfACarriedHandle))]
    public void DisposeCarrying()
    {
        _carrying?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _carrying = null;
    }

    public void Dispose()
    {
        DisposeStore();
        DisposeCarrying();
        GC.SuppressFinalize(this);
    }

    private static StoreDatasetType Empty() =>
        StoreDatasetType.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }).AsTask().GetAwaiter().GetResult();

    // N quads over N subjects and N objects, one predicate: the predicate is
    // the only term a second request with another predicate adds.
    private static CommitRequestBuilder Request(int count, string predicate)
    {
        CommitRequestBuilder request = new();
        RdfTerm p = Iri(predicate);

        for (int i = 0; i < count; i++)
        {
            string n = i.ToString(CultureInfo.InvariantCulture);
            request.Assert(Iri("s" + n), p, Iri("o" + n));
        }

        return request;
    }

    private static async Task<Commit> Second(StoreDatasetType dataset)
    {
        using CancellationTokenSource done = new();

        await foreach (Commit commit in dataset.Subscribe(new Position(1), SubscriptionFilter.All, done.Token))
        {
            if (commit.Position.Value == 2)
            {
                return commit;
            }
        }

        throw new InvalidOperationException("The second commit was not delivered.");
    }

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://ex/" + local));
}
