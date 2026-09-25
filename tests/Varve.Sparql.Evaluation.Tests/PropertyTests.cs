// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Compile;
using Varve.Sparql.Evaluation.Optimisation;
using Varve.Store;
using Xunit;
using static Varve.Sparql.Evaluation.Tests.Support;

namespace Varve.Sparql.Evaluation.Tests;

/// <summary>The evaluator's properties (<c>sparql-evaluation.md</c> §12.2).</summary>
public class PropertyTests
{
    /// <summary>The iterations of the optimiser property; stated in the milestone's report.</summary>
    internal const int OptimiserIterations = 20_000;

    /// <summary>
    /// §8.6: for generated data and queries, the optimised query and the one
    /// the optimiser never saw give the same solution multiset. Each rewrite
    /// must fire, or the property passed without testing it.
    /// </summary>
    [Fact]
    public void The_optimiser_changes_no_answer()
    {
        OptimiserCounts total = new();
        int parsed = 0;
        int answered = 0;
        Gen.Select(Generators.Data, Generators.Query).Sample(
            (data, text) =>
            {
                Query query = Parse(text);
                Interlocked.Increment(ref parsed);
                InMemoryDataset dataset = Generators.Load(data);
                Query normalised = new PathNormaliser().Rewrite(query);
                Optimiser optimiser = new(dataset, Options(optimise: false));
                Query optimised = optimiser.Optimise(normalised);
                lock (total)
                {
                    Add(total, optimiser.Counts);
                }

                List<string> expected = Evaluate(normalised, dataset);
                List<string> actual = Evaluate(optimised, dataset);
                if (expected.Count > 0 && expected[0] != "false")
                {
                    Interlocked.Increment(ref answered);
                }

                if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Only without the optimiser:\n  " + string.Join("\n  ", Minus(expected, actual)) + "\nOnly with it:\n  " + string.Join("\n  ", Minus(actual, expected)));
                }
            },
            iter: OptimiserIterations,
            print: t => t.Item2 + "\n-- data --\n" + Generators.Show(t.Item1));

        Assert.Equal(OptimiserIterations, parsed);

        // A property over empty answers tests nothing: about half of the generated queries have one, and the gate is two in five.
        Assert.True(answered > OptimiserIterations * 2 / 5, $"Only {answered} of {OptimiserIterations} generated queries had a non-empty answer.");
        Assert.True(total.FiltersPlaced > 0, "Filter placement never fired.");
        Assert.True(total.BgpsReordered > 0, "Triple order never fired.");
        Assert.True(total.JoinsReordered > 0, "Join order never fired.");
        Assert.True(total.ConstantsFolded > 0, "Constant folding never fired.");
        Assert.True(total.TrivialJoins > 0, "Trivial joins never fired.");
        TestContext.Current.SendDiagnosticMessage(
            $"Optimiser property: {OptimiserIterations} iterations, {answered} with a non-empty answer; fired: filters placed {total.FiltersPlaced}, BGPs reordered {total.BgpsReordered}, "
            + $"joins reordered {total.JoinsReordered}, constants folded {total.ConstantsFolded}, trivial joins {total.TrivialJoins}.");
    }

    /// <summary>The iterations of the store property; stated in the milestone's report.</summary>
    internal const int StoreIterations = 2_000;

    /// <summary>
    /// §12.2: for generated commit histories with retractions, and a position
    /// P, evaluation over the store's as-of view at P and over an
    /// <see cref="InMemoryDataset"/> loaded from that view's quads give the same
    /// solutions. The dataset is loaded from the view's own terms, so a blank
    /// node carries the store's label into both answers and the bijection
    /// between them is the identity.
    /// </summary>
    [Fact]
    public async Task The_store_as_of_a_position_answers_as_a_dataset_of_its_quads()
    {
        int answered = 0;
        await Gen.Select(Generators.History, Gen.Int[0, 1_000], Generators.Query).SampleAsync(
            async (history, at, text) =>
            {
                Query query = Parse(text);
                await using Varve.Store.Dataset store = await Varve.Store.Dataset.OpenAsync(new MemoryStorage(), new DatasetOptions { Clock = FixedClock.Instance });
                foreach ((bool Retract, GenQuad Quad)[] commit in history)
                {
                    CommitRequest request = new();
                    foreach ((bool retract, GenQuad quad) in commit)
                    {
                        RequestTerm s = quad.S, p = quad.P, o = quad.O;
                        _ = (retract, quad.G) switch
                        {
                            (false, null) => request.Assert(s, p, o),
                            (false, { } g) => request.Assert(s, p, o, g),
                            (true, null) => request.Retract(s, p, o),
                            (true, { } g) => request.Retract(s, p, o, g),
                        };
                    }

                    _ = await store.CommitAsync(request);
                }

                long position = at % (store.Head + 1);
                using DatasetView view = await store.AsOfAsync(position);
                InMemoryDataset copy = new();
                using (IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
                {
                    while (cursor.MoveNext())
                    {
                        Quad quad = cursor.Current;
                        RdfTerm? graph = quad.IsDefaultGraph ? null : Term(view, quad.Graph);
                        copy.Add(Term(view, quad.Subject), Term(view, quad.Predicate), Term(view, quad.Object), graph);
                    }
                }

                List<string> fromStore = Evaluate(query, view);
                List<string> fromDataset = Evaluate(query, copy);
                if (fromStore.Count > 0 && fromStore[0] != "false")
                {
                    Interlocked.Increment(ref answered);
                }

                if (!fromStore.SequenceEqual(fromDataset, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        "At position " + position + " of " + store.Head + ", only over the store:\n  " + string.Join("\n  ", Minus(fromStore, fromDataset))
                        + "\nOnly over the dataset:\n  " + string.Join("\n  ", Minus(fromDataset, fromStore)));
                }
            },
            iter: StoreIterations,
            print: t => t.Item3 + "\n-- position " + t.Item2 + " of history --\n" + string.Join("\n--\n", t.Item1.Select(c => string.Join("\n", c.Select(e => (e.Retract ? "- " : "+ ") + Generators.Show([e.Quad]))))));

        Assert.True(answered > StoreIterations * 3 / 10, $"Only {answered} of {StoreIterations} generated queries had a non-empty answer.");
        TestContext.Current.SendDiagnosticMessage($"Store property: {StoreIterations} iterations, {answered} with a non-empty answer.");
    }

    private static RdfTerm Term(DatasetView source, TermHandle handle) =>
        source.TryExternalise(handle, out RdfTerm? term) ? term : throw new InvalidOperationException("The view cannot externalise its own handle.");

    private static List<string> Evaluate(Query query, IQuadSource source)
    {
        using QueryResults results = new SparqlEvaluator(Options(optimise: false)).Evaluate(query, source);
        return results is BooleanResult boolean ? [boolean.Value ? "true" : "false"] : Rows(results);
    }

    private static List<string> Minus(List<string> left, List<string> right)
    {
        List<string> rest = [.. left];
        foreach (string row in right)
        {
            rest.Remove(row);
        }

        return rest;
    }

    private static void Add(OptimiserCounts total, OptimiserCounts one)
    {
        total.FiltersPlaced += one.FiltersPlaced;
        total.BgpsReordered += one.BgpsReordered;
        total.JoinsReordered += one.JoinsReordered;
        total.ConstantsFolded += one.ConstantsFolded;
        total.TrivialJoins += one.TrivialJoins;
    }
}
