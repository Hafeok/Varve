// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// The store's quad sources under the term-lookup contract (ADR 0022,
/// amended 2026-10-30; #191): a term denied is in no quad the source yields.
/// Each source reads the dictionary at its own position, whose runs hold
/// every entry up to it, so a denial is a term allocated after it.
/// </summary>
public class QuadSourceContractTests
{
    private static readonly RdfTerm Later = T.Iri("later");

    [Fact]
    public async Task a_pinned_view_never_denies_a_term_it_holds()
    {
        await using Dataset dataset = await FilledAsync();
        using DatasetView view = dataset.Pin();

        Assert.Contains(Triple(), QuadSourceContract.Holds(() => view, [Later]));
    }

    /// <summary>An as-of read is an overlay of the log tail on a checkpoint, both loaded before the view is handed out.</summary>
    [Fact]
    public async Task an_as_of_view_over_a_checkpoint_and_a_log_tail_never_denies_a_term_it_holds()
    {
        await using Dataset dataset = await FilledAsync();
        await dataset.CheckpointAsync(new Position(1), T.Ct);
        await dataset.CommitAsync(new CommitRequestBuilder().Assert(Later, T.Iri("p"), T.Literal("l")), T.Ct);

        for (long position = 1; position <= 2; position++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(position), T.Ct);
            IReadOnlyCollection<RdfTerm> yielded = QuadSourceContract.Holds(() => view, [Later]);
            Assert.Contains(Triple(), yielded);
            Assert.Equal(position == 2, System.Linq.Enumerable.Contains(yielded, Later));
        }
    }

    /// <summary>A staging view names terms no quad of its view holds; it yields its view's quads.</summary>
    [Fact]
    public async Task a_staging_view_never_denies_a_term_it_holds()
    {
        await using Dataset dataset = await FilledAsync();
        using DatasetView view = dataset.Pin();
        StagingView staging = view.Stage();
        staging.Stage(T.Iri("staged"));

        Assert.Contains(Triple(), QuadSourceContract.Holds(() => staging, [T.Iri("staged"), Later]));
    }

    /// <summary>
    /// A validator reads <c>Overlay(PendingSource, δ)</c> (spec T1 step 5),
    /// and δ may hold terms the head does not: a plain term the commit
    /// creates, a triple term the commit creates over terms the head holds,
    /// and one over a part the commit creates too.
    /// </summary>
    [Fact]
    public async Task the_source_a_validator_reads_never_denies_a_term_the_commit_creates()
    {
        await using Dataset dataset = await FilledAsync();
        Checking validator = new();

        CommitResult result = await dataset.CommitAsync(
            new CommitRequestBuilder { Validators = [validator] }
                .Assert(T.Iri("r2"), T.Iri("reifies"), RdfTerm.TripleTerm(T.Iri("ts"), T.Iri("tp"), T.Literal("fresh")))
                .Assert(T.Iri("r3"), T.Iri("reifies"), RdfTerm.TripleTerm(T.Iri("ts"), T.Iri("tp"), T.Literal("to2")))
                .Assert(T.Iri("r4"), T.Iri("reifies"), RdfTerm.TripleTerm(T.Iri("ts"), T.Iri("tp"), T.Literal("to"))),
            T.Ct);

        Assert.Equal(CommitOutcome.Committed, result.Outcome);
        validator.Failure?.Throw();
        Assert.Contains(RdfTerm.TripleTerm(T.Iri("ts"), T.Iri("tp"), T.Literal("fresh")), validator.Yielded);
        Assert.Contains(RdfTerm.TripleTerm(T.Iri("ts"), T.Iri("tp"), T.Literal("to2")), validator.Yielded);
    }

    private static RdfTerm Triple() => RdfTerm.TripleTerm(T.Iri("ts"), T.Iri("tp"), T.Literal("to"));

    /// <summary>One commit: a plain quad in a named graph, and a reifier of a triple term.</summary>
    private static async Task<Dataset> FilledAsync()
    {
        Dataset dataset = await T.Open(new MemoryStorage());
        await dataset.CommitAsync(
            new CommitRequestBuilder()
                .Assert(T.Iri("s"), T.Iri("p"), T.Literal("o"), T.Iri("g"))
                .Assert(T.Iri("r"), T.Iri("reifies"), Triple())
                .Assert(T.Iri("s"), T.Iri("p"), T.Literal("to2")),
            T.Ct);
        return dataset;
    }

    private sealed class Checking : ICommitValidator
    {
        public IReadOnlyCollection<RdfTerm> Yielded { get; private set; } = [];

        public System.Runtime.ExceptionServices.ExceptionDispatchInfo? Failure { get; private set; }

        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            try
            {
                Yielded = QuadSourceContract.Holds(() => proposed, [Later]);
            }
            catch (Exception exception)
            {
                Failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
            }

            return ValidationVerdict.Accept();
        }
    }
}
