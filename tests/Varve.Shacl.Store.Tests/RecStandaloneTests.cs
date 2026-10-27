// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
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
/// REC 4.1.0's shapes (ADR 0141), standalone: part 2a's early signal for the
/// REC gate of part 2b. The three files are read as an RDF merge, each file's
/// blank nodes its own, with <c>owl:Class rdfs:subClassOf rdfs:Class</c>
/// added (the maintainer's ruling on #13).
/// </summary>
public class RecStandaloneTests
{
    private static readonly ShaclOptions Options = new() { Extensions = [SparqlConstraints.Extension()], SubClassOfInShapesGraph = true, RefuseNonDeterministicFunctions = true };

    /// <summary>
    /// Brick's three SPARQL constraints name prefixes by <c>sh:prefixes</c>
    /// that no file declares with <c>sh:declare</c>, so the shapes graph is
    /// refused as it stands; the third also uses <c>owl:</c> without naming it.
    /// Reported upstream, with no exemption.
    /// </summary>
    [Fact]
    public void BricksSparqlConstraintsDeclareNoPrefixes()
    {
        ShapesLoad load = ShapesGraph.Load(Shapes(withSparql: true), GraphPattern.DefaultGraph, Options, TestContext.Current.CancellationToken);

        Assert.Null(load.Shapes);
        Assert.Equal(3, load.Problems.Count);
        Assert.All(load.Problems, p =>
        {
            Assert.Equal(ShaclProblemKind.ExtensionFailure, p.Kind);
            Assert.Contains("is not declared", p.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Without those three, the shapes load, none is classed full, and the
    /// head of the S portfolio conforms (generator version 2, whose leases
    /// carry <c>rec:leasor</c>). A full validation, so a long run.
    /// </summary>
    [Fact]
    public void TheSPortfolioConformsToRecsOtherShapes()
    {
        if (Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null)
        {
            Assert.Skip("A full validation of REC S: set VARVE_LONG_PROPERTIES.");
        }

        Assert.Equal(2, RecPortfolio.Version);
        ShapesLoad load = ShapesGraph.Load(Shapes(withSparql: false), GraphPattern.DefaultGraph, Options, TestContext.Current.CancellationToken);
        ShapesGraph shapes = Assert.IsType<ShapesGraph>(load.Shapes);
        Assert.NotEqual(Maintainability.Full, shapes.Classify().Worst);

        ValidationOutcome outcome = ShaclValidator.Validate(shapes, Data(Head(RecSize.S, held: 0).Quads), GraphPattern.Any, Options, TestContext.Current.CancellationToken);
        ValidationReport report = Assert.IsType<ValidationReport>(outcome.Report);
        Assert.True(report.Conforms, report.Results.Count + " results");
    }

    /// <summary>
    /// A <c>reject</c> binding of REC's other shapes over the S portfolio: the
    /// generator's next commit is accepted, and a violation made by an explicit
    /// mutation — a lease's <c>rec:leasor</c> retracted — is refused with
    /// <c>shapes-violated</c>. The violation comes from the test, never from
    /// the generator.
    /// </summary>
    [Fact]
    public async Task A_reject_binding_refuses_a_mutation_and_accepts_the_portfolio()
    {
        if (Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null)
        {
            Assert.Skip("REC S loaded into a store: set VARVE_LONG_PROPERTIES.");
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        (List<(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G)> quads, List<RecCommit> held) = Head(RecSize.S, held: 1);
        RdfTerm shapesGraph = RdfTerm.Iri("https://example.org/rec/shapes"u8);
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, ct);
        CommitRequestBuilder load = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm g) in quads)
        {
            load.Assert(s, p, o, g);
        }

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in ShapeTriples(withSparql: false))
        {
            load.Assert(s, p, o, shapesGraph);
        }

        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(load, ct)).Outcome);

        ShapesBinding binding = new(RdfTerm.Iri("https://example.org/rec/binding"u8), shapesGraph, GraphScope.Of([], ["https://example.org/rec/graph/"], DefaultGraphAccess.Excluded), ValidationMode.Reject) { SubClassOfInShapesGraph = true };
        ShapesGate gate = new([binding]);

        (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G) leasor = quads.First(q => q.P.Equals(RdfTerm.Iri("https://w3id.org/rec#leasor"u8)));
        CommitRequestBuilder mutation = new() { Validators = [gate] };
        mutation.Retract(leasor.S, leasor.P, leasor.O, leasor.G);
        CommitResult refused = await dataset.CommitAsync(mutation, ct);
        Assert.Equal(CommitOutcome.Rejected, refused.Outcome);
        Assert.Contains(refused.Report, t => Encoding.UTF8.GetString(t.Lexical).EndsWith("shapes-violated", StringComparison.Ordinal));

        CommitRequestBuilder next = new() { Validators = [gate] };
        foreach (RecQuad q in held[0].Asserted)
        {
            (RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm g) = Terms(q);
            next.Assert(s, p, o, g);
        }

        foreach (RecQuad q in held[0].Retracted)
        {
            (RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm g) = Terms(q);
            next.Retract(s, p, o, g);
        }

        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(next, ct)).Outcome);
    }

    private static InMemoryDataset Shapes(bool withSparql) => RecShapes.Dataset(withSparql, composed: false);

    private static List<(RdfTerm S, RdfTerm P, RdfTerm O)> ShapeTriples(bool withSparql) => RecShapes.Triples(withSparql, composed: false);

    private static (List<(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G)> Quads, List<RecCommit> Held) Head(RecSize size, int held) => RecShapes.Head(size, held);

    private static (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G) Terms(RecQuad q) => RecShapes.Terms(q);

    private static InMemoryDataset Data(List<(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G)> quads)
    {
        InMemoryDatasetBuilder data = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm g) in quads)
        {
            data.Add(s, p, o, g);
        }

        return data.ToDataset();
    }
}
