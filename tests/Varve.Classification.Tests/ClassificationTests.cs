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
using Varve.Store;
using Varve.Store.Log;
using Xunit;
using static Varve.Classification.Tests.Support;

namespace Varve.Classification.Tests;

/// <summary>
/// The shape-derived classifier (ADR 0232) and the classification projection
/// (ADR 0233): what annotated shapes classify, that the projection equals
/// classification from scratch at every position of generated histories,
/// that its cached state reads back, and that a damaged one is a miss.
/// </summary>
public class ClassificationTests
{
    /// <summary>Generated histories per run on the land gate; ten times as many in the nightly <c>long</c> job.</summary>
    internal static readonly int Iterations = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null ? 150 : 1_500;

    private static readonly string[] Nodes = ["n0", "n1", "n2", "n3", "n4"];
    private static readonly string[] Predicates = [TypeIri, Ex + "ownedBy", Ex + "partOf", Ex + "ssn", Ex + "label", Ex + "knows"];

    [Fact]
    public async Task Annotated_shapes_name_subjects_and_owners_transitively()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        await dataset.CommitAsync(Request(dataset, Shapes(bySsn: true, partOf: true, broken: false),
        [
            (IriOf(Ex + "alice"), IriOf(TypeIri), IriOf(Ex + "Person"), Classified[0], true),
            (IriOf(Ex + "bob"), IriOf(Ex + "ssn"), RdfTerm.Literal("1"u8), Classified[1], true),
            (IriOf(Ex + "lease"), IriOf(Ex + "ownedBy"), IriOf(Ex + "alice"), Classified[0], true),
            (IriOf(Ex + "room"), IriOf(Ex + "partOf"), IriOf(Ex + "lease"), Classified[1], true),
            (IriOf(Ex + "room"), IriOf(Ex + "ownedBy"), IriOf(Ex + "bob"), Classified[1], true),
            (IriOf(Ex + "carol"), IriOf(TypeIri), IriOf(Ex + "Person"), Outside, true),
        ]), ct);

        using DatasetView view = dataset.Pin();
        SortedSet<string> said = Signature(new ShapeClassifier([Binding]).Classify(view, QuadDelta.Empty), view);
        Assert.Contains("node http://ex/alice [http://ex/alice]", said);
        Assert.Contains("node http://ex/bob [http://ex/bob]", said);
        Assert.Contains("node http://ex/lease [http://ex/alice]", said);
        Assert.Contains("node http://ex/room [http://ex/alice,http://ex/bob]", said);
        Assert.Contains("public http://www.w3.org/1999/02/22-rdf-syntax-ns#type True", said);
        Assert.Contains("public http://ex/ownedBy False", said);
        Assert.Contains("graph http://ex/g/3 -1", said);
        Assert.Contains("graph http://ex/shapes -1", said);
    }

    [Fact]
    public async Task A_shapes_graph_that_does_not_classify_fails_closed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        await dataset.CommitAsync(Request(dataset, Shapes(bySsn: false, partOf: false, broken: true), [(IriOf(Ex + "x"), IriOf(Ex + "knows"), IriOf(Ex + "y"), Classified[0], true)]), ct);

        using DatasetView view = dataset.Pin();
        ShapeClassifier classifier = new([Binding]);
        Assert.NotEmpty(classifier.Problems(view));
        IRowClassification failed = classifier.Classify(view, QuadDelta.Empty);
        Assert.Contains("node http://ex/x [default]", Signature(failed, view));

        // A row-restricted caller sees nothing of the binding's graphs, only the shapes graph outside them; an unrestricted one sees all.
        RowScope restricted = RowScope.Of([new(Binding.Binding, SubjectScope.Of([IriOf(Ex + "x")], [], [], [], []))]);
        Assert.Equal(Shapes(false, false, true).Count, Count(new RowScopedQuadSource(view, failed, restricted)));
        Assert.Equal(1, Count(RowScopedQuadSource.Wrap(view, failed, RowScope.All)) - Shapes(false, false, true).Count);
    }

    /// <summary>
    /// ADR 0233's property: over generated histories of data toggled in and
    /// out and shapes replaced, the projection fed commit by commit equals
    /// classification from scratch at every position; its saved state reads
    /// back to the same classification; and a state with one byte changed,
    /// truncated or extended is a miss.
    /// </summary>
    [Fact]
    public async Task The_projection_equals_classification_from_scratch_at_every_position()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Gen<(RdfTerm, RdfTerm, RdfTerm, RdfTerm)> quad = Gen.Select(
            Gen.OneOfConst(Nodes), Gen.OneOfConst(Predicates), Gen.OneOfConst([.. Nodes, "Person", "lit"]), Gen.OneOfConst(0, 1, 2),
            (s, p, o, g) => (IriOf(Ex + s), IriOf(p), p == TypeIri ? IriOf(Ex + "Person") : o == "lit" ? RdfTerm.Literal("lit"u8) : IriOf(Ex + o), g < 2 ? Classified[g] : Outside));
        Gen<(bool, bool, bool)?> shapes = Gen.Frequency((6, Gen.Const(((bool, bool, bool)?)null)), (1, Gen.Select(Gen.Bool, Gen.Bool, Gen.Int[0, 5], (a, b, c) => ((bool, bool, bool)?)(a, b, c == 0))));
        Gen<((bool, bool, bool)? Shapes, (RdfTerm, RdfTerm, RdfTerm, RdfTerm)[] Toggles)> step = Gen.Select(shapes, quad.Array[1, 4], (s, t) => (s, t));
        int positions = 0;

        await Gen.Select(step.Array[1, 24], Gen.Int[0, int.MaxValue]).SampleAsync(async (steps, seed) =>
        {
            await using Dataset dataset = await CreateAsync(ct);
            HashSet<(RdfTerm, RdfTerm, RdfTerm, RdfTerm)> state = [];
            await dataset.CommitAsync(Request(dataset, Shapes(true, true, false), []), ct);

            foreach (((bool, bool, bool)? replace, (RdfTerm, RdfTerm, RdfTerm, RdfTerm)[] toggles) in steps)
            {
                List<(RdfTerm, RdfTerm, RdfTerm, RdfTerm, bool)> changes = [];

                foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm g) in toggles.Distinct())
                {
                    bool assert = state.Add((s, p, o, g));

                    if (!assert)
                    {
                        state.Remove((s, p, o, g));
                    }

                    changes.Add((s, p, o, g, assert));
                }

                List<(RdfTerm, RdfTerm, RdfTerm)>? shapesNow = replace is { } r ? Shapes(r.Item1, r.Item2, r.Item3) : null;
                await dataset.CommitAsync(Request(dataset, shapesNow, changes), ct);
            }

            ClassificationProjection projection = new(dataset, Binding);
            await dataset.CatchUpAsync(projection, ct);
            Assert.Equal(dataset.Head, projection.Position);

            MemoryStorage cache = new();
            await projection.SaveAsync(cache.Derived, ct);
            ClassificationProjection loaded = new(dataset, Binding);
            Assert.True(await loaded.LoadAsync(cache.Derived, ct));

            for (long p = 0; p <= dataset.Head.Value; p++)
            {
                using DatasetView view = await dataset.AsOfAsync(new Position(p), ct);
                SortedSet<string> expected = Signature(ShapeClassifier.Scratch(view, Binding), view);
                Assert.Equal(expected, Signature(new ShapeClassifier([Binding]).Classify(view, QuadDelta.Empty), view));
                Assert.True(projection.TryAt(new Position(p), view, out IRowClassification held));
                Assert.Equal(expected, Signature(held, view));
                Assert.True(loaded.TryAt(new Position(p), view, out IRowClassification reread));
                Assert.Equal(expected, Signature(reread, view));
                positions++;
            }

            Assert.False(projection.TryAt(new Position(dataset.Head.Value + 1), dataset.Pin(), out _));
            await AssertDamageIsAMissAsync(dataset, cache, seed, ct);
        }, iter: Iterations);

        TestContext.Current.SendDiagnosticMessage($"{positions} positions compared");
    }

    [Fact]
    public async Task A_reset_at_the_head_puts_the_floor_there_and_classifies_below_it_from_scratch()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        await dataset.CommitAsync(Request(dataset, Shapes(false, false, false), [(IriOf(Ex + "a"), IriOf(TypeIri), IriOf(Ex + "Person"), Classified[0], true)]), ct);
        await dataset.CommitAsync(Request(dataset, null, [(IriOf(Ex + "x"), IriOf(Ex + "ownedBy"), IriOf(Ex + "a"), Classified[1], true)]), ct);

        ClassificationProjection projection = new(dataset, Binding);
        using (DatasetView head = dataset.Pin())
        {
            await projection.ResetAsync(head, ct);
        }

        Assert.Equal(dataset.Head, projection.Floor);
        using DatasetView one = await dataset.AsOfAsync(new Position(1), ct);
        Assert.True(projection.TryAt(new Position(1), one, out IRowClassification below));
        Assert.Equal(Signature(ShapeClassifier.Scratch(one, Binding), one), Signature(below, one));

        Task<bool> waiting = projection.WaitForAsync(new Position(3), ct).AsTask();
        Assert.False(waiting.IsCompleted);
        await dataset.CommitAsync(Request(dataset, null, [(IriOf(Ex + "a"), IriOf(TypeIri), IriOf(Ex + "Person"), Classified[0], false)]), ct);
        await dataset.CatchUpAsync(projection, ct);
        Assert.True(await waiting);
        Assert.NotEmpty(projection.ChangedAt(new Position(3)));
    }

    // One byte changed, the blob cut short or grown: each is a miss, never a wrong row.
    private static async Task AssertDamageIsAMissAsync(Dataset dataset, MemoryStorage cache, int seed, CancellationToken ct)
    {
        BlobName name = (await cache.Derived.ListAsync(ct)).Single();
        byte[] bytes;
        using (IReadableBlob blob = await cache.Derived.OpenAsync(name, ct))
        {
            bytes = new byte[blob.Length.Value];
            Assert.Equal(bytes.Length, blob.Read(new ByteOffset(0), bytes));
        }

        Random random = new(seed);
        byte[] flipped = (byte[])bytes.Clone();
        flipped[random.Next(flipped.Length)] ^= (byte)(1 + random.Next(255));

        foreach (byte[] damaged in new[] { flipped, bytes[..^1], [.. bytes, 0] })
        {
            MemoryStorage other = new();
            await using (IBlobWriter writer = await other.Derived.CreateAsync(name, ct))
            {
                await writer.WriteAsync(damaged, ct);
                await writer.PublishAsync(ct);
            }

            Assert.False(await new ClassificationProjection(dataset, Binding).LoadAsync(other.Derived, ct));
        }
    }

    private static int Count(IQuadSource source)
    {
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        int count = 0;

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }
}
