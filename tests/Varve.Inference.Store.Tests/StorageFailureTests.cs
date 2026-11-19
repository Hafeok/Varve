// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Inference.Model;
using Varve.Inference.Store.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;
using static Varve.Inference.Store.Tests.Support;

namespace Varve.Inference.Store.Tests;

/// <summary>
/// What stops a binding and what does not (ADR 0195 point 6): a
/// store read that fails during a pass holds the pass off, before anything
/// changed, and the commit is applied again from the projection's position
/// once the store answers, with no generation; a failure no repeat can cure
/// stops the binding as failed, and nothing tries it again.
/// </summary>
public class StorageFailureTests
{
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly RdfTerm SubClassOf = IriOf(RdfsNs + "subClassOf");

    [Fact]
    public async Task A_store_read_that_fails_holds_the_pass_off_and_it_resumes_with_no_generation()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        InferenceBinding binding = Binding(InferenceProfile.Rdfs);
        await CommitAsync(dataset, [(Ex("C1"), SubClassOf, Ex("C2"), TBox, true), (Ex("a"), Type, Ex("C1"), Ex("g1"), true)], ct);
        InferenceProjection projection = new(dataset, binding);
        await dataset.CatchUpAsync(projection, ct);
        Generations made = projection.GenerationsMade;
        Position before = projection.Position;

        // The store fails while the next commit is applied: its new terms cannot be read.
        Failing store = new(new IOException("the store did not answer"));
        projection.ReadThrough = view => new FailingSource(view, store);
        await CommitAsync(dataset, [(Ex("b"), Type, Ex("C1"), Ex("g1"), true), (Ex("c"), Type, Ex("C1"), Ex("g2"), true)], ct);
        store.On = true;
        await Assert.ThrowsAsync<IOException>(async () => await dataset.CatchUpAsync(projection, ct));
        Assert.False(projection.Stopped);
        Assert.Equal(before, projection.Position);

        // The store answers again: the same commit is applied from where the projection was.
        store.On = false;
        await dataset.CatchUpAsync(projection, ct);
        Assert.False(projection.Stopped);
        Assert.Equal(dataset.Head, projection.Position);
        Assert.Equal(made, projection.GenerationsMade);
        Assert.Equal(InferenceReadOutcome.Held, projection.TryRead(dataset.Head, out InferredRows? rows));
        using DatasetView view = await dataset.AsOfAsync(dataset.Head, ct);
        Assert.Equal(Expected(view, binding), Text(rows!, view));
    }

    [Fact]
    public async Task A_failure_no_repeat_can_cure_stops_the_binding_as_failed_and_nothing_tries_it_again()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        InferenceBinding binding = Binding(InferenceProfile.Rdfs);
        await CommitAsync(dataset, [(Ex("C1"), SubClassOf, Ex("C2"), TBox, true), (Ex("a"), Type, Ex("C1"), Ex("g1"), true)], ct);
        InferenceProjection projection = new(dataset, binding);
        await dataset.CatchUpAsync(projection, ct);

        // Not the store's: a defect, which the same commit would meet again.
        Failing defect = new(new InvalidOperationException("an invariant broke")) { On = true };
        projection.ReadThrough = view => new FailingSource(view, defect);
        await CommitAsync(dataset, [(Ex("b"), Type, Ex("C1"), Ex("g1"), true)], ct);
        await dataset.CatchUpAsync(projection, ct);
        Assert.True(projection.Stopped);
        Assert.IsType<InvalidOperationException>(projection.Failure);
        Assert.Equal(InferenceReadOutcome.Unavailable, projection.TryRead(dataset.Head, out _));

        defect.On = false;
        await CommitAsync(dataset, [(Ex("C2"), SubClassOf, Ex("C3"), TBox, true)], ct);
        await dataset.CatchUpAsync(projection, ct);
        Assert.True(projection.Stopped);
        Assert.NotNull(projection.Failure);
    }

    /// <summary>Whether reads fail, and with what.</summary>
    private sealed class Failing(Exception error)
    {
        internal volatile bool On;

        internal void Check()
        {
            if (On)
            {
                throw error;
            }
        }
    }

    /// <summary>A view whose reads fail while <see cref="Failing.On"/> is set.</summary>
    private sealed class FailingSource(IQuadSource inner, Failing failing) : IQuadSource
    {
        public IEqualityComparer<TermHandle> TermComparer => inner.TermComparer;

        public bool TryInternalise(RdfTerm term, out TermHandle handle)
        {
            failing.Check();
            return inner.TryInternalise(term, out handle);
        }

        public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term)
        {
            failing.Check();
            return inner.TryExternalise(handle, out term);
        }

        public bool Contains(in Quad quad)
        {
            failing.Check();
            return inner.Contains(quad);
        }

        public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
        {
            failing.Check();
            return inner.Match(subject, predicate, @object, graph);
        }

        public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) => inner.Estimate(subject, predicate, @object, graph);

        public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => inner.TryGetInlineValue(handle, out value);
    }
}
