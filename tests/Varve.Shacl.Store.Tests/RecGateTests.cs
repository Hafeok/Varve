// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Shacl.Model;
using Varve.Shacl.Sparql;
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Shacl.Store.Tests;

/// <summary>
/// The REC gate (ADR 0176): REC 4.1.0's shapes in the gate's composition —
/// the three files, <c>owl:Class rdfs:subClassOf rdfs:Class</c> and the
/// prefix declarations Brick's SPARQL constraints need — validate generated
/// REC portfolios three ways, which agree: standalone, at commit time under
/// <c>reject</c>, and incrementally through the validation projection.
/// </summary>
public class RecGateTests
{
    private const string Sh = "http://www.w3.org/ns/shacl#";

    private static readonly bool Long = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is not null;

    private static readonly ShaclOptions Options = new() { Extensions = [SparqlConstraints.Extension()], SubClassOfInShapesGraph = true, RefuseNonDeterministicFunctions = true };

    private static readonly RdfTerm ShapesGraphName = RdfTerm.Iri("https://example.org/rec/shapes"u8);

    private static readonly ShapesBinding Binding = new(RdfTerm.Iri("https://example.org/rec/binding"u8), ShapesGraphName, GraphScope.Of([], ["https://example.org/rec/graph/"], DefaultGraphAccess.Excluded), ValidationMode.Reject) { SubClassOfInShapesGraph = true };

    [Fact]
    public void The_composed_shapes_load_with_brick_s_sparql_constraints_and_none_is_full()
    {
        ShapesLoad load = ShapesGraph.Load(RecShapes.Dataset(withSparql: true, composed: true), GraphPattern.DefaultGraph, Options, TestContext.Current.CancellationToken);
        Assert.Empty(load.Problems);
        ShapesGraph shapes = Assert.IsType<ShapesGraph>(load.Shapes);
        Assert.NotEqual(Maintainability.Full, shapes.Classify().Worst);
    }

    /// <summary>
    /// Which SHACL features REC uses, the class each falls in, and the shapes
    /// that cannot be evaluated: <c>testdata/rec/gate/features.md</c>, pinned.
    /// <c>VARVE_WRITE_GOLDEN=1</c> writes it again, for a pull request that
    /// says why it changed.
    /// </summary>
    [Fact]
    public void The_features_report_is_the_pinned_one()
    {
        string report = FeaturesReport();
        string path = Path.Combine(RecShapes.Root(), "testdata", "rec", "gate", "features.md");
        if (Environment.GetEnvironmentVariable("VARVE_WRITE_GOLDEN") == "1")
        {
            File.WriteAllText(path, report);
        }

        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), report);
    }

    /// <summary>
    /// REC S: every commit of the generated history is accepted by a
    /// <c>reject</c> gate, and fed to the projection as it lands; at every
    /// 25th position and the last (every position, long), the projection's
    /// report equals the standalone validation there byte for byte, and the
    /// commit's attachment is the digest of the projection's slice.
    /// </summary>
    [Fact]
    public async Task REC_S_standalone_commit_time_and_projection_agree()
    {
        long last = RecPortfolio.Shape(RecSize.S).Commits + 1;
        (int compared, TimeSpan elapsed, TimeSpan comparing) = await RunAsync(RecSize.S, p => Long || p % 25 == 0 || p == last, TestContext.Current.CancellationToken);
        TestContext.Current.SendDiagnosticMessage($"REC S: {last} commits through the gate and the projection, {compared} positions compared, in {elapsed.TotalSeconds:F1} s, {comparing.TotalSeconds:F1} s of it comparing");
    }

    /// <summary>REC M, long only: 100 positions spread over the history, the last among them.</summary>
    [Fact]
    public async Task REC_M_standalone_commit_time_and_projection_agree_at_100_positions()
    {
        if (!Long)
        {
            Assert.Skip("REC M through the gate and the projection: set VARVE_LONG_PROPERTIES.");
        }

        long last = RecPortfolio.Shape(RecSize.M).Commits + 1;
        HashSet<long> positions = [last];
        for (int i = 1; i < 100; i++)
        {
            positions.Add(Math.Max(2, last * i / 100));
        }

        (int compared, TimeSpan elapsed, TimeSpan comparing) = await RunAsync(RecSize.M, positions.Contains, TestContext.Current.CancellationToken);
        TestContext.Current.SendDiagnosticMessage($"REC M: {last} commits through the gate and the projection, {compared} positions compared, in {elapsed.TotalSeconds:F1} s, {comparing.TotalSeconds:F1} s of it comparing");
    }

    /// <summary>
    /// The shapes at position 1, then the portfolio's history, every commit
    /// through a reject gate and fed to the projection as it lands, the way a
    /// server's projection keeps up; at each <paramref name="sampled"/>
    /// position, while it is the head, the three ways compared.
    /// </summary>
    private static async Task<(int Compared, TimeSpan Elapsed, TimeSpan Comparing)> RunAsync(RecSize size, Func<long, bool> sampled, CancellationToken ct)
    {
        Stopwatch clock = Stopwatch.StartNew();
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, ct);
        ValidationProjection projection = new(dataset, Binding, Options);
        await using IAsyncEnumerator<Commit> feed = dataset.Subscribe(new Position(0), SubscriptionFilter.All, ct).GetAsyncEnumerator(ct);
        ShapesGate gate = new([Binding], Options);
        int compared = 0;
        Stopwatch comparing = new();

        CommitRequest shapes = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in RecShapes.Triples(withSparql: true, composed: true))
        {
            shapes.Assert(s, p, o, ShapesGraphName);
        }

        await LandAsync(shapes);
        foreach (RecCommit commit in RecPortfolio.Generate(size, 20261015))
        {
            CommitRequest request = new() { Validators = [gate] };
            foreach (RecQuad q in commit.Asserted)
            {
                (RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm g) = RecShapes.Terms(q);
                request.Assert(s, p, o, g);
            }

            foreach (RecQuad q in commit.Retracted)
            {
                (RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm g) = RecShapes.Terms(q);
                request.Retract(s, p, o, g);
            }

            await LandAsync(request);
        }

        return (compared, clock.Elapsed, comparing.Elapsed);

        async Task LandAsync(CommitRequest request)
        {
            CommitResult result = await dataset.CommitAsync(request, ct);
            Assert.True(result.Outcome == CommitOutcome.Committed, "Commit " + (dataset.Head.Value + 1) + " was refused: " + string.Join(" ", result.Report.Select(t => Encoding.UTF8.GetString(t.Lexical))));
            Assert.True(await feed.MoveNextAsync());
            Commit landed = feed.Current;
            await projection.ApplyAsync(landed, ct);
            if (sampled(landed.Position.Value))
            {
                comparing.Start();
                Compare(dataset, projection, landed, ct);
                comparing.Stop();
                compared++;
            }
        }
    }

    /// <summary>At the head: standalone against the projection, and the commit's attachment against the projection's slice.</summary>
    private static void Compare(Dataset dataset, ValidationProjection projection, Commit commit, CancellationToken ct)
    {
        using DatasetView view = dataset.Pin();
        Assert.Equal(commit.Position, view.Position);
        ValidationOutcome standalone = BindingValidation.Validate(Binding, view, Options, ct);
        ValidationReport expected = Assert.IsType<ValidationReport>(standalone.Report);
        Assert.True(projection.TryReportAt(commit.Position, out ValidationOutcome projected));
        Assert.Equal(Encoding.UTF8.GetString(expected.ToCanonicalNQuads().Span), Encoding.UTF8.GetString(projected.Report!.ToCanonicalNQuads().Span));

        // Commit time: the shapes commit carries no attachment; every data commit carries the projection's slice.
        if (commit.Position.Value == 1)
        {
            Assert.Equal(0, commit.Attachments.Length);
            return;
        }

        ShaclOptions options = BindingValidation.Options(Binding, Options);
        ShapesLoad load = BindingValidation.Load(Binding, view, options);
        DataGraphSource data = new(view, Binding);
        IncrementalOutcome outcome = IncrementalValidation.Compare(load.Shapes!, data, GraphPattern.Any, BindingValidation.InDataGraph(data, commit.Delta), options, ct);
        ValidationReport slice = projection.RestrictedReportAt(commit.Position, outcome.Comparison!.Pairs)!;
        Assert.True(commit.TryExternalise(commit.Attachments.Span[0], out RdfTerm? attachment));
        Assert.Equal(ShapesGate.Attachment(Binding, slice), attachment);
    }

    /// <summary>The features report: every SHACL term the composed shapes graph uses, its role and class, the classification, and what is not evaluated.</summary>
    private static string FeaturesReport()
    {
        List<(RdfTerm S, RdfTerm P, RdfTerm O)> triples = RecShapes.Triples(withSparql: true, composed: true);
        SortedDictionary<string, int> uses = new(StringComparer.Ordinal);
        RdfTerm type = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);
        foreach ((RdfTerm _, RdfTerm p, RdfTerm o) in triples)
        {
            Count(p);
            if (p.Equals(type))
            {
                Count(o);
            }
        }

        void Count(RdfTerm term)
        {
            string iri = Encoding.UTF8.GetString(term.Lexical);
            if (term.Kind == RdfTermKind.Iri && iri.StartsWith(Sh, StringComparison.Ordinal))
            {
                string name = "sh:" + iri[Sh.Length..];
                uses[name] = uses.GetValueOrDefault(name) + 1;
            }
        }

        ShapesLoad load = ShapesGraph.Load(RecShapes.Dataset(withSparql: true, composed: true), GraphPattern.DefaultGraph, Options, TestContext.Current.CancellationToken);
        ShapesClassification classification = load.Shapes!.Classify();

        StringBuilder text = new();
        text.Append("# The REC gate's features report\n\n");
        text.Append("REC 4.1.0's shapes in the gate's composition (ADR 0176): `rec.ttl`, `Brick+patches.ttl`,\n");
        text.Append("`recimports.ttl` and `testdata/rec/gate/composition.ttl`, read under\n");
        text.Append("`varve:subClassOfInShapesGraph` with SHACL-SPARQL. Written by\n");
        text.Append("`RecGateTests.The_features_report_is_the_pinned_one` and pinned by it.\n\n");
        text.Append("## Shapes by class (ADR 0171)\n\n| Class | Shapes |\n|---|---:|\n");
        foreach (Maintainability m in (Maintainability[])[Maintainability.Incremental, Maintainability.Scoped, Maintainability.Full])
        {
            text.Append("| ").Append(m).Append(" | ").Append(classification.Shapes.Count(s => s.Maintainability == m).ToString(CultureInfo.InvariantCulture)).Append(" |\n");
        }

        // A reason a shape inherits names the shape it refers to; the construct is the innermost one.
        text.Append("\n## What puts a shape in its class\n\n| Construct | Section | Class | Shapes, by their own construct | Shapes, by a shape they refer to |\n|---|---|---|---:|---:|\n");
        foreach (IGrouping<(string Construct, string Section, Maintainability Class), (ShapeClassification Shape, bool Inherited)> group in classification.Shapes
            .SelectMany(s => s.Reasons.Select(r => (Reason: r, Shape: s)))
            .GroupBy(x => (Construct: Innermost(x.Reason.Construct), x.Reason.Section, Class: x.Reason.Maintainability), x => (x.Shape, Inherited: x.Reason.Construct.StartsWith("refers to ", StringComparison.Ordinal)))
            .OrderBy(g => g.Key.Construct, StringComparer.Ordinal).ThenBy(g => g.Key.Section, StringComparer.Ordinal))
        {
            int own = group.Where(x => !x.Inherited).Select(x => x.Shape).Distinct().Count();
            int inherited = group.Where(x => x.Inherited).Select(x => x.Shape).Distinct().Count();
            text.Append("| ").Append(group.Key.Construct).Append(" | ").Append(group.Key.Section).Append(" | ").Append(group.Key.Class)
                .Append(" | ").Append(own.ToString(CultureInfo.InvariantCulture)).Append(" | ").Append(inherited.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
        }

        text.Append("\n## The SHACL terms used\n\n| Term | Uses | Role | Class |\n|---|---:|---|---|\n");
        foreach ((string term, int count) in uses)
        {
            (string role, string cls) = Roles.TryGetValue(term, out (string, string) known) ? known : ("**not classified**", "—");
            text.Append("| `").Append(term).Append("` | ").Append(count.ToString(CultureInfo.InvariantCulture)).Append(" | ").Append(role).Append(" | ").Append(cls).Append(" |\n");
        }

        text.Append("\n## Shapes that cannot be evaluated\n\n");
        text.Append(load.Problems.Count == 0
            ? "None: the composed shapes graph loads with no problem.\n"
            : string.Join("\n", load.Problems.Select(p => "- " + p.Kind + ": " + p.Message)) + "\n");
        text.Append("\nNot evaluated, by design: the SHACL-AF rules (`sh:rule`, `sh:TripleRule`, `sh:SPARQLRule` and\n");
        text.Append("their terms), which are inference and belong to milestone 9 (ADR 0149). A validation reads them as\n");
        text.Append("triples of the shapes graph and nothing more.\n");
        return text.ToString();
    }

    /// <summary>A construct without the "refers to … by …: " of the shapes it was inherited through.</summary>
    private static string Innermost(string construct)
    {
        while (construct.StartsWith("refers to ", StringComparison.Ordinal) && construct.IndexOf(": ", StringComparison.Ordinal) is int at and > 0)
        {
            construct = construct[(at + 2)..];
        }

        return construct;
    }

    /// <summary>Each SHACL term's role and class, from ADR 0171's table; a term missing here is reported, and the report changes.</summary>
    private static readonly Dictionary<string, (string Role, string Class)> Roles = new(StringComparer.Ordinal)
    {
        ["sh:NodeShape"] = ("node shape (Core §3.2), with implicit class targets when also an `rdfs:Class` (§3.1.3.3)", "incremental on `rdf:type`; scoped on a subclass change"),
        ["sh:PropertyShape"] = ("property shape (Core §3.3)", "the class of its path and constraints"),
        ["sh:property"] = ("shape-based (Core §7.8.2)", "the class of the shapes referenced"),
        ["sh:path"] = ("predicate path (Core §4.1)", "incremental"),
        ["sh:targetClass"] = ("class target (Core §3.1.3.2)", "incremental on `rdf:type`; scoped on a subclass change"),
        ["sh:targetSubjectsOf"] = ("subjects-of target (Core §3.1.3.4)", "incremental"),
        ["sh:targetObjectsOf"] = ("objects-of target (Core §3.1.3.5)", "incremental"),
        ["sh:minCount"] = ("cardinality (Core §7.2.1)", "incremental"),
        ["sh:maxCount"] = ("cardinality (Core §7.2.2)", "incremental"),
        ["sh:class"] = ("value type (Core §7.1.1)", "incremental on `rdf:type`; scoped on a subclass change"),
        ["sh:datatype"] = ("value type (Core §7.1.2)", "incremental"),
        ["sh:nodeKind"] = ("value type (Core §7.1.3)", "incremental"),
        ["sh:IRI"] = ("a node kind (Core §7.1.3)", "—"),
        ["sh:Literal"] = ("a node kind (Core §7.1.3)", "—"),
        ["sh:minInclusive"] = ("value range (Core §7.3.2)", "incremental"),
        ["sh:maxInclusive"] = ("value range (Core §7.3.4)", "incremental"),
        ["sh:minLength"] = ("string-based (Core §7.4.1)", "incremental"),
        ["sh:in"] = ("other (Core §7.9.3)", "incremental"),
        ["sh:or"] = ("logical (Core §7.7.3)", "the class of the shapes referenced"),
        ["sh:and"] = ("logical (Core §7.7.2)", "the class of the shapes referenced"),
        ["sh:not"] = ("logical (Core §7.7.1)", "the class of the shapes referenced"),
        ["sh:qualifiedValueShape"] = ("qualified value shape (Core §7.8.4)", "the class of the shape referenced"),
        ["sh:qualifiedMinCount"] = ("qualified value shape (Core §7.8.4)", "the class of the shape referenced"),
        ["sh:qualifiedMaxCount"] = ("qualified value shape (Core §7.8.4)", "the class of the shape referenced"),
        ["sh:qualifiedValueShapesDisjoint"] = ("qualified value shape, with its siblings (Core §7.8.4)", "the class of the shapes referenced"),
        ["sh:defaultValue"] = ("value nodes of a property shape (Core §6.9.2)", "incremental"),
        ["sh:severity"] = ("severity (Core §3.1.4)", "static"),
        ["sh:Warning"] = ("a severity (Core §3.1.4)", "static"),
        ["sh:message"] = ("message (Core §3.1.5)", "static"),
        ["sh:name"] = ("non-validating (Core §8.1)", "static"),
        ["sh:description"] = ("non-validating (Core §8.1)", "static"),
        ["sh:sparql"] = ("SPARQL-based constraint (SHACL-SPARQL §3)", "scoped when analysable (ADR 0171)"),
        ["sh:SPARQLConstraint"] = ("SPARQL-based constraint (SHACL-SPARQL §3)", "scoped when analysable (ADR 0171)"),
        ["sh:select"] = ("SPARQL-based constraint's query (SHACL-SPARQL §3)", "—"),
        ["sh:prefixes"] = ("prefix declarations (SHACL-SPARQL §2)", "—"),
        ["sh:declare"] = ("prefix declarations (SHACL-SPARQL §2), the composition's", "—"),
        ["sh:prefix"] = ("prefix declarations (SHACL-SPARQL §2), the composition's", "—"),
        ["sh:namespace"] = ("prefix declarations (SHACL-SPARQL §2), the composition's", "—"),
        ["sh:rule"] = ("SHACL-AF rule", "not evaluated (ADR 0149)"),
        ["sh:TripleRule"] = ("SHACL-AF rule", "not evaluated (ADR 0149)"),
        ["sh:SPARQLRule"] = ("SHACL-AF rule", "not evaluated (ADR 0149)"),
        ["sh:construct"] = ("SHACL-AF rule's query", "not evaluated (ADR 0149)"),
        ["sh:condition"] = ("SHACL-AF rule's condition", "not evaluated (ADR 0149)"),
        ["sh:subject"] = ("SHACL-AF triple rule's subject", "not evaluated (ADR 0149)"),
        ["sh:predicate"] = ("SHACL-AF triple rule's predicate", "not evaluated (ADR 0149)"),
        ["sh:object"] = ("SHACL-AF triple rule's object", "not evaluated (ADR 0149)"),
        ["sh:this"] = ("SHACL-AF node expression, the focus node", "not evaluated (ADR 0149)"),
    };
}
