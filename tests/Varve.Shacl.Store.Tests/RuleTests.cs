// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Shacl.Store.Tests;

/// <summary>ADR 0169's rules one at a time, and the problems' wire shape.</summary>
public class RuleTests
{
    private const string Integers = "ex:S sh:targetSubjectsOf ex:p0 ; sh:property [ sh:path ex:p0 ; sh:datatype xsd:integer ] .";

    private const string Full = "ex:S sh:targetNode ex:n0 ; sh:property [ sh:path ex:p0 ; sh:values ex:k ; sh:node ex:T ] . ex:T sh:nodeKind sh:IRI .";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (RdfTerm, RdfTerm, RdfTerm, bool)[] Data(string turtle, bool assert = true) =>
        [.. Support.Triples(turtle).Select(t => (t.S, t.P, t.O, assert))];

    [Fact]
    public async Task Reject_refuses_a_new_violation_with_its_findings()
    {
        await using Dataset dataset = await Support.CreateAsync(new ShapesGate([Support.Binding(ValidationMode.Reject)]), Ct);
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, Integers, Data("ex:n0 ex:p0 1 .")), Ct)).Outcome);

        CommitResult refused = await dataset.CommitAsync(Support.Request(dataset, null, Data("ex:n1 ex:p0 \"x\" .")), Ct);
        Assert.Equal(CommitOutcome.Rejected, refused.Outcome);
        Assert.Equal("https://w3id.org/varve/problems/shapes-violated", Encoding.UTF8.GetString(refused.Report[0].Lexical));
        using JsonDocument members = JsonDocument.Parse(refused.Report[1].Lexical.ToArray());
        JsonElement finding = Assert.Single(members.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal("<http://example.com/ns#n1>", finding.GetProperty("focusNode").GetString());
        Assert.Equal("<http://example.com/ns#p0>", finding.GetProperty("path").GetString());
        Assert.Equal("\"x\"", finding.GetProperty("value").GetString());
        Assert.Equal("<http://www.w3.org/ns/shacl#DatatypeConstraintComponent>", finding.GetProperty("component").GetString());
        Assert.False(members.RootElement.GetProperty("shapesChanged").GetBoolean());
        Assert.Equal("<http://example.com/ns#binding>", members.RootElement.GetProperty("binding").GetString());
    }

    [Fact]
    public async Task Reject_accepts_an_edit_of_an_entity_already_non_conforming()
    {
        await using Dataset dataset = await Support.CreateAsync(new ShapesGate([Support.Binding(ValidationMode.Report)]), Ct);
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, Integers, Data("ex:n0 ex:p0 \"bad\" .")), Ct)).Outcome);

        // The same data under reject: the violation exists before, and an unrelated edit of n0 keeps it, so nothing new.
        await using Dataset rejecting = await Support.CreateAsync(new ShapesGate([Support.Binding(ValidationMode.Reject)]), Ct);
        Assert.Equal(CommitOutcome.Committed, (await rejecting.CommitAsync(Support.Request(rejecting, "ex:S sh:targetNode ex:none .", Data("ex:n0 ex:p0 \"bad\" .")), Ct)).Outcome);
        Assert.Equal(CommitOutcome.Committed, (await rejecting.CommitAsync(Support.Request(rejecting, Integers, []), Ct)).Outcome);
        Assert.Equal(CommitOutcome.Committed, (await rejecting.CommitAsync(Support.Request(rejecting, null, Data("ex:n0 ex:label \"renamed\" .")), Ct)).Outcome);
        Assert.False(Support.Full(rejecting).Report!.Conforms);
    }

    [Fact]
    public async Task A_shapes_change_is_never_refused_for_existing_data_and_its_data_is_checked_against_the_new_shapes()
    {
        await using Dataset dataset = await Support.CreateAsync(new ShapesGate([Support.Binding(ValidationMode.Reject)]), Ct);
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, "ex:S sh:targetNode ex:none .", Data("ex:n0 ex:p0 \"bad\" .")), Ct)).Outcome);

        // Tightening the shapes over existing bad data is accepted.
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, Integers, []), Ct)).Outcome);

        // Tightening and adding bad data in one commit refuses for the new data, and says the shapes changed too.
        CommitResult refused = await dataset.CommitAsync(Support.Request(dataset, Integers + " ex:S2 sh:targetSubjectsOf ex:p1 ; sh:property [ sh:path ex:p1 ; sh:datatype xsd:integer ] .", Data("ex:n2 ex:p1 \"x\" .")), Ct);
        Assert.Equal(CommitOutcome.Rejected, refused.Outcome);
        using JsonDocument members = JsonDocument.Parse(refused.Report[1].Lexical.ToArray());
        Assert.True(members.RootElement.GetProperty("shapesChanged").GetBoolean());
        Assert.Contains("new shapes", members.RootElement.GetProperty("detail").GetString(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reject_binding_with_a_full_shape_is_refused_at_apply_time_unless_it_allows_full_evaluation()
    {
        await using Dataset dataset = await Support.CreateAsync(new ShapesGate([]), Ct);
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, Full, []), Ct)).Outcome);
        using DatasetView view = dataset.Pin();

        BindingCheck refused = new ShapesGate([]).Check(Support.Binding(ValidationMode.Reject), view);
        Assert.False(refused.Accepted);
        Assert.Equal("https://w3id.org/varve/problems/binding-not-incremental", Encoding.UTF8.GetString(refused.Refusal![0].Lexical));
        using JsonDocument members = JsonDocument.Parse(refused.Refusal[1].Lexical.ToArray());
        JsonElement reason = members.RootElement.GetProperty("shapes").EnumerateArray().First();
        Assert.Equal("<http://example.com/ns#S>", reason.GetProperty("shape").GetString());
        Assert.Contains("§3.3", reason.GetProperty("section").GetString(), System.StringComparison.Ordinal);
        Assert.Contains("varve:allowFullEvaluation", members.RootElement.GetProperty("detail").GetString(), System.StringComparison.Ordinal);

        Assert.True(new ShapesGate([]).Check(Support.Binding(ValidationMode.Reject, allowFull: true), view).Accepted);
        Assert.True(new ShapesGate([]).Check(Support.Binding(ValidationMode.Report), view).Accepted);
        Assert.Equal(Maintainability.Full, new ShapesGate([]).Check(Support.Binding(ValidationMode.Report), view).Classification!.Worst);
    }

    [Fact]
    public async Task A_reject_binding_whose_shapes_became_full_fails_closed()
    {
        await using Dataset dataset = await Support.CreateAsync(new ShapesGate([Support.Binding(ValidationMode.Reject)]), Ct);
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, Integers, []), Ct)).Outcome);

        // The shapes commit that makes them full is refused, about the shapes.
        CommitResult refused = await dataset.CommitAsync(Support.Request(dataset, Full, []), Ct);
        Assert.Equal("https://w3id.org/varve/problems/binding-not-incremental", Encoding.UTF8.GetString(refused.Report[0].Lexical));

        // Under allowFullEvaluation the same commit lands.
        await using Dataset allowing = await Support.CreateAsync(new ShapesGate([Support.Binding(ValidationMode.Reject, allowFull: true)]), Ct);
        Assert.Equal(CommitOutcome.Committed, (await allowing.CommitAsync(Support.Request(allowing, Full, Data("ex:n0 ex:p0 ex:a .")), Ct)).Outcome);
    }

    [Fact]
    public async Task An_unstratifiable_or_non_deterministic_shapes_graph_is_shapes_invalid()
    {
        await using Dataset dataset = await Support.CreateAsync(new ShapesGate([Support.Binding(ValidationMode.Report)]), Ct);
        CommitResult cycle = await dataset.CommitAsync(Support.Request(dataset, "ex:S sh:targetNode ex:n0 ; sh:not ex:S .", []), Ct);
        Assert.Equal(CommitOutcome.Rejected, cycle.Outcome);
        Assert.Equal("https://w3id.org/varve/problems/shapes-invalid", Encoding.UTF8.GetString(cycle.Report[0].Lexical));
        using JsonDocument members = JsonDocument.Parse(cycle.Report[1].Lexical.ToArray());
        Assert.Equal("UnstratifiableRecursion", members.RootElement.GetProperty("problems")[0].GetProperty("kind").GetString());

        CommitResult now = await dataset.CommitAsync(
            Support.Request(dataset, "ex:S sh:targetNode ex:n0 ; sh:sparql [ sh:select \"SELECT $this WHERE { FILTER(NOW() > \\\"2026-01-01T00:00:00Z\\\"^^<http://www.w3.org/2001/XMLSchema#dateTime>) }\" ] .", []),
            Ct);
        Assert.Equal(CommitOutcome.Rejected, now.Outcome);
        Assert.Contains("NOW()", Encoding.UTF8.GetString(now.Report[1].Lexical), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task Off_runs_nothing_and_attaches_nothing()
    {
        await using Dataset dataset = await Support.CreateAsync(new ShapesGate([Support.Binding(ValidationMode.Off)]), Ct);
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, "ex:S sh:targetNode ex:n0 ; sh:not ex:S .", Data("ex:n0 ex:p0 \"x\" .")), Ct)).Outcome);
        Assert.Empty(await Support.HeadAttachmentsAsync(dataset, Ct));
    }

    [Fact]
    public async Task The_data_graph_leaves_out_the_shapes_graph_and_the_varve_graphs()
    {
        await using Dataset dataset = await Support.CreateAsync(new ShapesGate([Support.Binding(ValidationMode.Reject)]), Ct);

        // A shape that would fail every subject of sh:targetNode-carrying triples, were the shapes graph data.
        const string shapes = "ex:S sh:targetSubjectsOf sh:targetSubjectsOf ; sh:closed true .";
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, shapes, []), Ct)).Outcome);

        CommitRequest reserved = new();
        reserved.Assert(RequestTerm.FromTerm(Support.Ex("x")), RequestTerm.FromTerm(RdfTerm.Iri("http://www.w3.org/ns/shacl#targetSubjectsOf"u8)), RequestTerm.FromTerm(Support.Ex("y")), RequestTerm.FromTerm(RdfTerm.Iri("https://w3id.org/varve/ns#setup"u8)));
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(reserved, Ct)).Outcome);

        CommitRequest data = new();
        data.Assert(RequestTerm.FromTerm(Support.Ex("x")), RequestTerm.FromTerm(RdfTerm.Iri("http://www.w3.org/ns/shacl#targetSubjectsOf"u8)), RequestTerm.FromTerm(Support.Ex("y")), RequestTerm.FromTerm(Support.DataGraph));
        Assert.Equal(CommitOutcome.Rejected, (await dataset.CommitAsync(data, Ct)).Outcome);
    }

    [Fact]
    public async Task Two_bindings_each_attach_in_the_order_of_their_IRIs()
    {
        ShapesBinding second = new(Support.Ex("b2"), Support.ShapesGraph, GraphScope.All, ValidationMode.Report);
        ShapesBinding first = new(Support.Ex("b1"), Support.ShapesGraph, GraphScope.All, ValidationMode.Reject);
        await using Dataset dataset = await Support.CreateAsync(new ShapesGate([second, first]), Ct);
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, Integers, Data("ex:n0 ex:p0 1 .")), Ct)).Outcome);
        List<RdfTerm> attachments = await Support.HeadAttachmentsAsync(dataset, Ct);
        Assert.Equal([Support.Ex("b1"), Support.Ex("b2")], attachments.Select(a => a.Subject!));
        Assert.Equal(attachments[0].Object, attachments[1].Object);
    }

    [Fact]
    public async Task A_bulk_commit_is_gated_and_carries_no_attachment()
    {
        await using Dataset dataset = await Support.CreateAsync(new ShapesGate([Support.Binding(ValidationMode.Reject)]), Ct);
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Support.Request(dataset, Integers, []), Ct)).Outcome);

        await using (BulkLoad bad = await dataset.BeginBulkLoadAsync(null, Ct))
        {
            bad.Assert(Support.Ex("n0"), Support.Ex("p0"), RdfTerm.Literal("x"u8), Support.DataGraph);
            Assert.Equal(CommitOutcome.Rejected, (await bad.CommitAsync(new CommitMetadata(), Ct)).Outcome);
        }

        await using BulkLoad good = await dataset.BeginBulkLoadAsync(null, Ct);
        good.Assert(Support.Ex("n0"), Support.Ex("p0"), RdfTerm.Literal("1"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8)), Support.DataGraph);
        Assert.Equal(CommitOutcome.Committed, (await good.CommitAsync(new CommitMetadata(), Ct)).Outcome);
        Assert.Empty(await Support.HeadAttachmentsAsync(dataset, Ct));
    }
}
