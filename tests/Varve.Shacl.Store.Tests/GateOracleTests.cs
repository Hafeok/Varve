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
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Shacl.Store.Tests;

/// <summary>
/// ADR 0169's R1 and R5 against a full-validation oracle: over generated
/// shapes and generated commits — data toggles, and shapes replaced, alone or
/// with data — a <c>reject</c> binding refuses exactly the commits whose full
/// report after, under the commit's shapes, has a violation its full report
/// before, under the same shapes, does not; an accepted commit records one
/// attachment, whose digest the same comparison computed after the commit
/// reproduces.
/// </summary>
public class GateOracleTests
{
    /// <summary>Generated histories per run on the land gate, as reported in the pull request.</summary>
    internal static readonly int Iterations = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null ? 500 : 5_000;

    private static readonly string[] Nodes = ["ex:n0", "ex:n1", "ex:n2"];
    private static readonly string[] Predicates = ["ex:p0", "ex:p1"];

    private static readonly Gen<string> Constraint = Gen.OneOf(
        Gen.Const("sh:minCount 1"),
        Gen.Const("sh:maxCount 1"),
        Gen.Const("sh:class ex:C0"),
        Gen.Const("sh:datatype xsd:integer"),
        Gen.Const("sh:node ex:T"),
        Gen.Const("sh:not ex:T"),
        Gen.Const("sh:nodeKind sh:IRI"));

    private static readonly Gen<string> ShapesText =
        from target in Gen.OneOf(Gen.OneOfConst(Nodes).Select(n => "sh:targetNode " + n), Gen.Const("sh:targetClass ex:C0"), Gen.OneOfConst(Predicates).Select(p => "sh:targetSubjectsOf " + p))
        from path in Gen.OneOfConst(Predicates)
        from constraints in Constraint.Array[1, 2]
        from t in Gen.OneOf(Gen.Const("sh:class ex:C1"), Gen.Const("sh:property [ sh:path ex:p1 ; sh:minCount 1 ]"))
        select "ex:S a sh:NodeShape ; " + target + " ; sh:property [ sh:path " + path + " ; " + string.Join(" ; ", constraints.Distinct()) + " ] .\n"
            + "ex:T a sh:NodeShape ; " + t + " .\n";

    private static readonly Gen<(string S, string P, string O)> Triple = Gen.OneOf(
        Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst(Predicates), Gen.OneOfConst([.. Nodes, "1", "\"x\""])).Select((s, p, o) => (s, p, o)),
        Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst("ex:C0", "ex:C1")).Select((s, c) => (s, "rdf:type", c)));

    private sealed record Step(string? Shapes, (string S, string P, string O)[] Toggles);

    private static readonly Gen<Step> Steps = Gen.Frequency(
        (6, Triple.Array[1, 3].Select(t => new Step(null, t))),
        (1, ShapesText.Select(s => new Step(s, []))),
        (1, Gen.Select(ShapesText, Triple.Array[1, 2]).Select((s, t) => new Step(s, t))));

    [Fact]
    public async Task A_reject_binding_refuses_exactly_the_commits_that_introduce_a_violation()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int refused = 0, accepted = 0;
        await Gen.Select(ShapesText, Steps.Array[1, 8]).SampleAsync(async (initial, steps) =>
        {
            ShapesGate gate = new([Support.Binding(ValidationMode.Reject)]);
            await using Dataset dataset = await Support.CreateAsync(gate, ct);
            Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, initial, []), ct)).Outcome);
            string shapes = initial;
            HashSet<(string, string, string)> state = [];
            foreach (Step step in steps)
            {
                List<(RdfTerm, RdfTerm, RdfTerm, bool)> toggles = [];
                HashSet<(string, string, string)> next = [.. state];
                foreach ((string s, string p, string o) in step.Toggles.Distinct())
                {
                    bool assert = next.Add((s, p, o));
                    if (!assert)
                    {
                        next.Remove((s, p, o));
                    }

                    (RdfTerm ts, RdfTerm tp, RdfTerm to) = Support.Triples($"{s} {p} {o} .")[0];
                    toggles.Add((ts, tp, to, assert));
                }

                // The oracle: under the commit's shapes, the data before and the data after, in full.
                string commitShapes = step.Shapes ?? shapes;
                HashSet<string> before = Support.Violations(Support.Full(dataset, commitShapes).Report!);
                CommitResult result = await dataset.CommitAsync(Support.Request(dataset, step.Shapes, toggles), ct);

                if (result.Outcome == CommitOutcome.Rejected)
                {
                    // Nothing landed: apply the toggles to a scratch copy to see what the oracle would have said.
                    await using Dataset scratch = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, ct);
                    await Replay(scratch, commitShapes, next, ct);
                    HashSet<string> wouldBe = Support.Violations(Support.Full(scratch, commitShapes).Report!);
                    Assert.True(wouldBe.Except(before).Any(), "Refused a commit that introduces no violation.\n" + initial + "\n" + string.Join("\n", steps.Select(s => s.ToString())));
                    refused++;
                    continue;
                }

                Assert.Equal(CommitOutcome.Committed, result.Outcome);
                HashSet<string> after = Support.Violations(Support.Full(dataset, commitShapes).Report!);
                Assert.False(after.Except(before).Any(), "Accepted a commit that introduces " + string.Join(", ", after.Except(before)) + "\n" + initial + "\n" + string.Join("\n", steps.Select(s => s.ToString())));
                List<RdfTerm> attachments = await Support.HeadAttachmentsAsync(dataset, ct);
                RdfTerm attachment = Assert.Single(attachments);
                Assert.Equal(RdfTermKind.TripleTerm, attachment.Kind);
                Assert.Equal(Support.Ex("binding"), attachment.Subject);
                state = next;
                shapes = commitShapes;
                accepted++;
            }
        }, iter: Iterations);

        Assert.True(refused > 0 && accepted > 0, $"{refused} refused and {accepted} accepted: the generator should produce both.");
        TestContext.Current.SendDiagnosticMessage($"{refused} commits refused, {accepted} accepted");
    }

    [Fact]
    public async Task The_attachment_is_the_digest_the_comparison_gives_after_the_commit()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ShapesGate gate = new([Support.Binding(ValidationMode.Report)]);
        await using Dataset dataset = await Support.CreateAsync(gate, ct);
        const string shapes = "ex:S sh:targetClass ex:C0 ; sh:property [ sh:path ex:p0 ; sh:minCount 1 ; sh:datatype xsd:integer ] .";
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, shapes, []), ct)).Outcome);

        // A blank node the commit creates is labelled by the id it is committed with, so the digest holds after the commit.
        List<(RdfTerm, RdfTerm, RdfTerm, bool)> data = [.. Support.Triples("_:b a ex:C0 ; ex:p0 \"x\" . ex:n1 a ex:C0 .").Select(t => (t.S, t.P, t.O, true))];
        CommitRequest request = Support.Request(dataset, null, data);
        CommitResult result = await dataset.CommitAsync(request, ct);
        Assert.Equal(CommitOutcome.Committed, result.Outcome);
        RdfTerm attachment = Assert.Single(await Support.HeadAttachmentsAsync(dataset, ct));

        // Recompute: the same comparison, over the state after the commit and its delta, read back from the log.
        Commit commit = await LastAsync(dataset, ct);
        using DatasetView view = dataset.Pin();
        ShapesBinding binding = Support.Binding(ValidationMode.Report);
        ShapesGate recompute = new([binding]);
        ValidationVerdict verdict = recompute.Validate(view, commit.Delta);
        Assert.Equal(attachment, Assert.Single(verdict.Attachments));
        Assert.StartsWith("sha256:", Encoding.UTF8.GetString(attachment.Object!.Lexical), StringComparison.Ordinal);
    }

    private static async Task<Commit> LastAsync(Dataset dataset, CancellationToken ct)
    {
        await foreach (Commit commit in dataset.Subscribe(new Position(dataset.Head.Value - 1), SubscriptionFilter.All, ct))
        {
            return commit;
        }

        throw new InvalidOperationException("No commit.");
    }

    private static async Task Replay(Dataset scratch, string shapes, HashSet<(string S, string P, string O)> state, CancellationToken ct)
    {
        List<(RdfTerm, RdfTerm, RdfTerm, bool)> data = [];
        foreach ((string s, string p, string o) in state)
        {
            (RdfTerm ts, RdfTerm tp, RdfTerm to) = Support.Triples($"{s} {p} {o} .")[0];
            data.Add((ts, tp, to, true));
        }

        Assert.Equal(CommitOutcome.Committed, (await scratch.CommitAsync(Support.Request(scratch, shapes, data), ct)).Outcome);
    }
}
