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
/// matched. Readings are <see cref="AllocationMeter"/>'s (issue #32).
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
        InMemoryDatasetBuilder builder = new();
        for (int i = 0; i < count; i++)
        {
            builder.Add(Named("s" + i), Named("p"), Named("o" + i));
        }

        return builder.ToDataset();
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

    // One execution, its results returned so that they escape at every tier.
    private static Func<object?> Run(Query query, IQuadSource source) => () =>
    {
        QueryResults results = new SparqlEvaluator(Options()).Evaluate(query, source);
        SolutionResults solutions = (SolutionResults)results;

        while (solutions.MoveNext())
        {
        }

        results.Dispose();
        return results;
    };

    private static long Difference(Query query, IQuadSource small, IQuadSource large)
    {
        (long smallCost, long largeCost) = AllocationMeter.MeasurePair(Run(query, small), Run(query, large));
        return largeCost - smallCost;
    }

    [Fact]
    public void Over_the_dataset_a_solution_costs_its_row_and_an_unmatched_quad_nothing()
    {
        InMemoryDataset small = Data(Small), large = Data(Large);
        long perSolution = Difference(Matching, small, large) / (Large - Small);
        Assert.Equal(RowBytes, perSolution);

        Assert.Equal(0, Difference(Unmatched, small, large));
    }

    [Fact]
    public async Task Over_the_store_a_solution_costs_its_row_and_an_unmatched_quad_nothing()
    {
        (Varve.Store.Dataset smallStore, DatasetView small) = await StoreOf(Small);
        (Varve.Store.Dataset largeStore, DatasetView large) = await StoreOf(Large);
        try
        {
            long perSolution = Difference(Matching, small, large) / (Large - Small);
            Assert.Equal(RowBytes, perSolution);

            Assert.Equal(0, Difference(Unmatched, small, large));
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
