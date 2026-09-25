// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Store;
using Xunit;
using static Varve.Sparql.Evaluation.Tests.Support;

namespace Varve.Sparql.Evaluation.Tests;

/// <summary>
/// §11: what a <c>Bgp</c> query allocates, as the difference between two runs
/// of different sizes — so the fixed cost of compiling and opening cancels —
/// divided by the difference in solutions, and zero for quads scanned and not
/// matched. Each side is measured three times and the smallest reading counts,
/// as in the parser's and the store's allocation tests: a stray allocation
/// only ever adds.
/// </summary>
public class AllocationTests
{
    private const int Small = 1_000;
    private const int Large = 5_000;

    /// <summary>
    /// One solution row for a query of two slots: <c>ulong[3]</c>, two slots and
    /// one mask word, 24 bytes of array header and 24 of payload.
    /// </summary>
    private const long RowBytes = 48;

    private static readonly Query Matching = Parse("SELECT ?s ?o { ?s :p ?o }");

    // ?s :p ?s scans every :p quad and keeps those whose ends are equal: none here.
    private static readonly Query Unmatched = Parse("SELECT ?s { ?s :p ?s }");

    private static InMemoryDataset Data(int count)
    {
        InMemoryDataset dataset = new();
        for (int i = 0; i < count; i++)
        {
            dataset.Add(Named("s" + i), Named("p"), Named("o" + i));
        }

        return dataset;
    }

    private static async Task<(Varve.Store.Dataset Store, DatasetView View)> StoreOf(int count)
    {
        Varve.Store.Dataset store = await Varve.Store.Dataset.OpenAsync(new MemoryStorage(), new DatasetOptions { Clock = FixedClock.Instance });
        CommitRequest request = new();
        for (int i = 0; i < count; i++)
        {
            request.Assert(Named("s" + i), Named("p"), Named("o" + i));
        }

        _ = await store.CommitAsync(request);
        return (store, store.Pin());
    }

    private static long Measure(Query query, IQuadSource source)
    {
        SparqlEvaluator evaluator = new(Options());
        long best = long.MaxValue;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            using (QueryResults results = evaluator.Evaluate(query, source))
            {
                SolutionResults solutions = (SolutionResults)results;
                while (solutions.MoveNext())
                {
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // The first run warms up whatever is lazily built once per source.
            if (attempt > 0)
            {
                best = Math.Min(best, allocated);
            }
        }

        return best;
    }

    [Fact]
    public void Over_the_dataset_a_solution_costs_its_row_and_an_unmatched_quad_nothing()
    {
        long perSolution = (Measure(Matching, Data(Large)) - Measure(Matching, Data(Small))) / (Large - Small);
        Assert.Equal(RowBytes, perSolution);

        Assert.Equal(0, Measure(Unmatched, Data(Large)) - Measure(Unmatched, Data(Small)));
    }

    [Fact]
    public async Task Over_the_store_a_solution_costs_its_row_and_an_unmatched_quad_nothing()
    {
        (Varve.Store.Dataset smallStore, DatasetView small) = await StoreOf(Small);
        (Varve.Store.Dataset largeStore, DatasetView large) = await StoreOf(Large);
        try
        {
            long perSolution = (Measure(Matching, large) - Measure(Matching, small)) / (Large - Small);
            Assert.Equal(RowBytes, perSolution);

            Assert.Equal(0, Measure(Unmatched, large) - Measure(Unmatched, small));
        }
        finally
        {
            small.Dispose();
            large.Dispose();
            await smallStore.DisposeAsync();
            await largeStore.DisposeAsync();
        }
    }
}
