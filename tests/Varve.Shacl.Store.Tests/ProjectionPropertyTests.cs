// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Shacl.Store.Tests;

/// <summary>
/// ADR 0174's property: at every position of generated histories — data
/// asserted and retracted, shapes replaced alone and with data — the
/// projection's report equals <see cref="BindingValidation.Validate"/> over
/// <see cref="Dataset.AsOfAsync"/> there, byte for byte in canonical
/// N-Quads; and each data commit's attachment, recorded by a <c>report</c>
/// gate at commit time, is the digest of the projection's rows at that
/// position restricted to the commit's affected pairs (ADR 0173 point 3).
/// </summary>
public class ProjectionPropertyTests
{
    /// <summary>Generated histories per run on the land gate, as reported in the pull request.</summary>
    internal static readonly int Iterations = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null ? 200 : 2_000;

    private static readonly string[] Nodes = ["ex:n0", "ex:n1", "ex:n2", "ex:n3"];
    private static readonly string[] Predicates = ["ex:p0", "ex:p1"];

    private static readonly Gen<string> Constraint = Gen.OneOf(
        Gen.Const("sh:minCount 1"),
        Gen.Const("sh:maxCount 1"),
        Gen.Const("sh:class ex:C0"),
        Gen.Const("sh:datatype xsd:integer"),
        Gen.Const("sh:node ex:T"),
        Gen.Const("sh:not ex:T"),
        Gen.Const("sh:nodeKind sh:IRI"),
        Gen.Const("sh:in ( ex:n0 1 )"),
        Gen.Const("sh:severity sh:Warning"));

    private static readonly Gen<string> PathText = Gen.OneOf(
        Gen.OneOfConst(Predicates),
        Gen.Const("[ sh:inversePath ex:p0 ]"),
        Gen.Const("( ex:p0 ex:p1 )"),
        Gen.Const("[ sh:zeroOrMorePath ex:p1 ]"),
        Gen.Const("[ sh:alternativePath ( ex:p0 [ sh:oneOrMorePath ex:p1 ] ) ]"),
        Gen.Const("( [ sh:zeroOrOnePath ex:p0 ] ex:p1 )"));

    private static readonly Gen<string> ShapesText =
        from target in Gen.OneOf(
            Gen.OneOfConst(Nodes).Select(n => "sh:targetNode " + n),
            Gen.Const("sh:targetClass ex:C0"),
            Gen.OneOfConst(Predicates).Select(p => "sh:targetSubjectsOf " + p),
            Gen.OneOfConst(Predicates).Select(p => "sh:targetObjectsOf " + p))
        from path in PathText
        from constraints in Constraint.Array[1, 3]
        from t in Gen.OneOf(Gen.Const("sh:class ex:C1"), Gen.Const("sh:property [ sh:path ex:p1 ; sh:minCount 1 ]"))
        from closed in Gen.Bool
        select "ex:S a sh:NodeShape ; " + target + " ; sh:property [ sh:path " + path + " ; " + string.Join(" ; ", constraints.Distinct()) + " ] .\n"
            + "ex:T a sh:NodeShape ; " + t + " .\n"
            + (closed ? "ex:U a sh:NodeShape ; sh:targetClass ex:C1 ; sh:closed true ; sh:ignoredProperties ( rdf:type ) ; sh:property [ sh:path ex:p0 ] .\n" : string.Empty);

    private static readonly Gen<(string S, string P, string O)> Triple = Gen.OneOf(
        Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst(Predicates), Gen.OneOfConst([.. Nodes, "1", "\"x\""])).Select((s, p, o) => (s, p, o)),
        Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst("ex:C0", "ex:C1")).Select((s, c) => (s, "rdf:type", c)),
        Gen.Const(("ex:C1", "rdfs:subClassOf", "ex:C0")));

    private sealed record Step(string? Shapes, (string S, string P, string O)[] Toggles);

    private static readonly Gen<Step> Steps = Gen.Frequency(
        (8, Triple.Array[1, 4].Select(t => new Step(null, t))),
        (1, ShapesText.Select(s => new Step(s, []))),
        (1, Gen.Select(ShapesText, Triple.Array[1, 2]).Select((s, t) => new Step(s, t))));

    [Fact]
    public async Task The_report_at_every_position_is_the_full_validation_there()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int positions = 0, digests = 0;
        await Gen.Select(ShapesText, Steps.Array[1, 40], Gen.Bool).SampleAsync(async (initial, steps, shapesFirst) =>
        {
            ShapesBinding binding = Support.Binding(ValidationMode.Report);
            ShapesGate gate = new([binding]);
            await using Dataset dataset = await Support.CreateAsync(gate, ct);
            HashSet<(string, string, string)> state = [];

            // The shapes arrive first, or after some data.
            if (shapesFirst)
            {
                Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, initial, []), ct)).Outcome);
            }

            foreach (Step step in steps)
            {
                List<(RdfTerm, RdfTerm, RdfTerm, bool)> toggles = [];
                foreach ((string s, string p, string o) in step.Toggles.Distinct())
                {
                    bool assert = state.Add((s, p, o));
                    if (!assert)
                    {
                        state.Remove((s, p, o));
                    }

                    (RdfTerm ts, RdfTerm tp, RdfTerm to) = Support.Triples($"{s} {p} {o} .")[0];
                    toggles.Add((ts, tp, to, assert));
                }

                Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, step.Shapes, toggles), ct)).Outcome);
            }

            if (!shapesFirst)
            {
                Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, initial, []), ct)).Outcome);
            }

            ValidationProjection projection = new(dataset, binding);
            await dataset.CatchUpAsync(projection, ct);
            Assert.Equal(dataset.Head, projection.Position);

            for (long p = 0; p <= dataset.Head.Value; p++)
            {
                Position at = new(p);
                using DatasetView view = await dataset.AsOfAsync(at, ct);
                ValidationOutcome expected = BindingValidation.Validate(binding, view, null, ct);
                Assert.True(projection.TryReportAt(at, out ValidationOutcome actual));
                Assert.Equal(Bytes(expected), Bytes(actual));
                positions++;
            }

            // The local cache's snapshot reads back to the same report at every position.
            MemoryStorage cache = new();
            await projection.SaveAsync(cache.Derived, ct);
            ValidationProjection loaded = new(dataset, binding);
            Assert.True(await loaded.LoadAsync(cache.Derived, ct));
            Assert.Equal(projection.Position, loaded.Position);
            for (long p = 0; p <= dataset.Head.Value; p++)
            {
                Assert.True(projection.TryReportAt(new Position(p), out ValidationOutcome original));
                Assert.True(loaded.TryReportAt(new Position(p), out ValidationOutcome reread));
                Assert.Equal(Bytes(original), Bytes(reread));
            }

            // Commit time against the projection: each data commit's digest is the projection's slice.
            await foreach (Commit commit in dataset.Subscribe(new Position(0), SubscriptionFilter.All, ct))
            {
                using DatasetView view = await dataset.AsOfAsync(commit.Position, ct);
                if (BindingValidation.Touches(view, commit.Delta, binding.ShapesGraph))
                {
                    if (commit.Position == dataset.Head)
                    {
                        break;
                    }

                    continue;
                }

                ShapesLoad load = BindingValidation.Load(binding, view, BindingValidation.Options(binding, null));
                DataGraphSource data = new(view, binding);
                IncrementalOutcome outcome = IncrementalValidation.Compare(load.Shapes!, data, GraphPattern.Any, BindingValidation.InDataGraph(data, commit.Delta), BindingValidation.Options(binding, null), ct);
                ValidationReport slice = projection.RestrictedReportAt(commit.Position, outcome.Comparison!.Pairs)!;
                Assert.True(commit.TryExternalise(commit.Attachments.Span[0], out RdfTerm? attachment));
                Assert.Equal(ShapesGate.Attachment(binding, slice), attachment);
                digests++;
                if (commit.Position == dataset.Head)
                {
                    break;
                }
            }
        }, iter: Iterations);

        TestContext.Current.SendDiagnosticMessage($"{positions} positions compared, {digests} digests checked");
    }

    [Fact]
    public async Task A_projection_fed_commit_by_commit_serves_positions_as_it_reaches_them()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ShapesBinding binding = Support.Binding(ValidationMode.Report);
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, ct);
        ValidationProjection projection = new(dataset, binding);
        Assert.True(projection.TryReportAt(new Position(0), out ValidationOutcome empty));
        Assert.True(empty.Report!.Conforms);

        await dataset.CommitAsync(Support.Request(dataset, "ex:S sh:targetNode ex:n0 ; sh:property [ sh:path ex:p0 ; sh:minCount 1 ] .", []), ct);
        Assert.False(projection.TryReportAt(new Position(1), out _));

        Task waiting = projection.WaitForAsync(new Position(1), ct).AsTask();
        Assert.False(waiting.IsCompleted);
        await dataset.CatchUpAsync(projection, ct);
        await waiting;

        Assert.True(projection.TryReportAt(new Position(1), out ValidationOutcome one));
        ShaclResult result = Assert.Single(one.Report!.Results);
        Assert.Equal(Support.Ex("n0"), result.FocusNode);

        using CancellationTokenSource soon = new(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await projection.WaitForAsync(new Position(2), soon.Token));
    }

    [Fact]
    public async Task A_shapes_graph_that_does_not_load_is_served_as_its_problems_until_fixed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ShapesBinding binding = Support.Binding(ValidationMode.Report);
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, ct);
        await dataset.CommitAsync(Support.Request(dataset, "ex:S sh:targetNode ex:n0 ; sh:minCount \"x\" .", []), ct);
        await dataset.CommitAsync(Support.Request(dataset, null, [(Support.Ex("n0"), Support.Ex("p0"), Support.Ex("n1"), true)]), ct);
        await dataset.CommitAsync(Support.Request(dataset, "ex:S sh:targetNode ex:n0 ; sh:property [ sh:path ex:p1 ; sh:minCount 1 ] .", []), ct);
        ValidationProjection projection = new(dataset, binding);
        await dataset.CatchUpAsync(projection, ct);

        for (long p = 1; p <= 3; p++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(p), ct);
            ValidationOutcome expected = BindingValidation.Validate(binding, view, null, ct);
            Assert.True(projection.TryReportAt(new Position(p), out ValidationOutcome actual));
            Assert.Equal(expected.Succeeded, actual.Succeeded);
            Assert.Equal(Bytes(expected), Bytes(actual));
        }

        Assert.True(projection.TryReportAt(new Position(2), out ValidationOutcome failed));
        Assert.False(failed.Succeeded);
        Assert.True(projection.TryReportAt(new Position(3), out ValidationOutcome fixedUp));
        Assert.Single(fixedUp.Report!.Results);
    }

    /// <summary>
    /// The saved state on a file dataset's local cache, whose blob names
    /// carry <c>enc(binding)</c>: read back after the storage is reopened.
    /// </summary>
    [Fact]
    public async Task A_snapshot_reads_back_from_a_file_datasets_cache()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        // A binding IRI with a '~', which enc leaves and no backend takes in a name.
        ShapesBinding binding = new(RdfTerm.Iri("http://example.com/~team/binding"u8), Support.ShapesGraph, GraphScope.All, ValidationMode.Report);
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, ct);
        await dataset.CommitAsync(Support.Request(dataset, "ex:S sh:targetClass ex:C0 ; sh:property [ sh:path ex:p0 ; sh:minCount 1 ] .", []), ct);
        await dataset.CommitAsync(Support.Request(dataset, null, [(Support.Ex("n0"), Support.Triples("ex:a a ex:b .")[0].P, Support.Ex("C0"), true)]), ct);
        ValidationProjection first = new(dataset, binding);
        await dataset.CatchUpAsync(first, ct);
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "varve-validation-" + Guid.NewGuid().ToString("N"));

        try
        {
            await using (FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System }, ct))
            {
                await first.SaveAsync(storage.Derived, ct);
            }

            await using FileStorage reopened = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System }, ct);
            ValidationProjection second = new(dataset, binding);
            Assert.True(await second.LoadAsync(reopened.Derived, ct));
            Assert.Equal(dataset.Head, second.Position);
        }
        finally
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_loaded_snapshot_continues_and_another_bindings_is_ignored()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ShapesBinding binding = Support.Binding(ValidationMode.Report);
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, ct);
        await dataset.CommitAsync(Support.Request(dataset, "ex:S sh:targetClass ex:C0 ; sh:property [ sh:path ex:p0 ; sh:minCount 1 ] .", []), ct);
        await dataset.CommitAsync(Support.Request(dataset, null, [(Support.Ex("n0"), Support.Triples("ex:a a ex:b .")[0].P, Support.Ex("C0"), true)]), ct);
        ValidationProjection first = new(dataset, binding);
        await dataset.CatchUpAsync(first, ct);
        MemoryStorage cache = new();
        await first.SaveAsync(cache.Derived, ct);
        await first.SaveAsync(cache.Derived, ct);
        Assert.Single(await cache.Derived.ListAsync(ct));

        // The next commits, after a restart: the loaded projection catches up from its position.
        await dataset.CommitAsync(Support.Request(dataset, null, [(Support.Ex("n0"), Support.Ex("p0"), Support.Ex("n1"), true)]), ct);
        await dataset.CommitAsync(Support.Request(dataset, null, [(Support.Ex("n1"), Support.Triples("ex:a a ex:b .")[0].P, Support.Ex("C0"), true)]), ct);
        ValidationProjection second = new(dataset, binding);
        Assert.True(await second.LoadAsync(cache.Derived, ct));
        Assert.Equal(new Position(2), second.Position);
        await dataset.CatchUpAsync(second, ct);
        for (long p = 0; p <= dataset.Head.Value; p++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(p), ct);
            Assert.True(second.TryReportAt(new Position(p), out ValidationOutcome actual));
            Assert.Equal(Bytes(BindingValidation.Validate(binding, view, null, ct)), Bytes(actual));
        }

        // Another binding of the same IRI — other graphs — does not read it, nor does a damaged blob.
        ShapesBinding other = new(binding.Binding, binding.ShapesGraph, GraphScope.Of([Support.DataGraph], [], DefaultGraphAccess.Excluded), ValidationMode.Report);
        Assert.False(await new ValidationProjection(dataset, other).LoadAsync(cache.Derived, ct));
        MemoryStorage damaged = new();
        BlobName name = Assert.Single(await cache.Derived.ListAsync(ct));
        await using (IBlobWriter writer = await damaged.Derived.CreateAsync(name, ct))
        {
            await writer.WriteAsync("VVP1"u8.ToArray(), ct);
            await writer.PublishAsync(ct);
        }

        Assert.False(await new ValidationProjection(dataset, binding).LoadAsync(damaged.Derived, ct));
    }

    [Fact]
    public async Task A_cold_start_begins_at_the_head_and_holds_nothing_below_it()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ShapesBinding binding = Support.Binding(ValidationMode.Report);
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, ct);
        await dataset.CommitAsync(Support.Request(dataset, "ex:S sh:targetSubjectsOf ex:p0 ; sh:property [ sh:path ex:p0 ; sh:datatype xsd:integer ] .", []), ct);
        await dataset.CommitAsync(Support.Request(dataset, null, [(Support.Ex("n0"), Support.Ex("p0"), Support.Ex("n1"), true)]), ct);

        ValidationProjection projection = new(dataset, binding);
        using (DatasetView head = dataset.Pin())
        {
            await projection.ResetAsync(head, ct);
        }

        Assert.Equal(new Position(2), projection.Floor);
        Assert.Equal(new Position(2), projection.Position);
        Assert.False(projection.TryReportAt(new Position(1), out _));

        await dataset.CommitAsync(Support.Request(dataset, null, [(Support.Ex("n2"), Support.Ex("p0"), Support.Ex("n1"), true)]), ct);
        await dataset.CatchUpAsync(projection, ct);
        for (long p = 2; p <= 3; p++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(p), ct);
            Assert.True(projection.TryReportAt(new Position(p), out ValidationOutcome actual));
            Assert.Equal(Bytes(BindingValidation.Validate(binding, view, null, ct)), Bytes(actual));
        }

        Assert.True(projection.TryReportAt(new Position(3), out ValidationOutcome three));
        Assert.Equal(2, three.Report!.Results.Count);
    }

    /// <summary>The canonical N-Quads of a report, or the problems' kinds and messages.</summary>
    private static string Bytes(ValidationOutcome outcome) => outcome.Report is { } report
        ? Encoding.UTF8.GetString(report.ToCanonicalNQuads().Span)
        : string.Join("\n", outcome.Problems.Select(p => p.Kind + " " + p.Message));
}
