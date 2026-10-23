// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Varve.Delta.Model;
using Varve.Events.Model;
using Varve.Management;
using Varve.Management.Model;
using Varve.Protocol;
using Varve.Rdf;
using Varve.Shacl;
using Xunit;

namespace Varve.Events.Tests;

/// <summary>
/// The fixture of moved declarations, <c>fixtures/moved-declarations/</c>
/// (ADR 0167): the code validators accept it in its new home, its event nodes
/// read in the catalogue graphs' form as they did in the old one, and ADR
/// 0157's shapes conform on them in both homes. SHACL part 2b's agreement
/// property takes the same fixture.
/// </summary>
public sealed class MovedFixtureTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "moved-declarations", "notes.trig");

    [Fact]
    public void the_fixture_is_valid_in_its_new_home_and_reads_as_its_catalogue_did()
    {
        List<Q> quads = Trig.Quads(File.ReadAllText(Fixture));
        Declarations declarations = DeclarationReader.Read(Trig.Load(quads));
        Assert.Empty(declarations.Findings);
        Assert.Equal(EventDeclarationState.Moved, declarations.Datasets["notes"].EventDeclarations);

        List<DeclarationProblem> problems = [];
        List<FeedChange> catalogue = ManagedEventDeclarations.ToCatalogue(quads.Select(q => (q.S, q.P, q.O)), problems);
        Assert.Empty(problems);
        Assert.Equal(2, catalogue.Select(c => c.Graph).Distinct().Count());
        Assert.True(EventDeclarationCheck.Check(Trig.Load(catalogue.Select(c => new Q(c.Subject, c.Predicate, c.Object, c.Graph))), catalogue).Accepted);

        DeclarationSet read = EventDeclarations.ReadAll(Trig.Load(catalogue.Select(c => new Q(c.Subject, c.Predicate, c.Object, c.Graph))));
        Assert.Empty(read.Problems);
        Assert.Equal(["NoteCreated", "NoteShared"], read.Types.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void the_event_shapes_conform_on_the_nodes_in_both_homes()
    {
        List<Q> quads = Trig.Quads(File.ReadAllText(Fixture));
        List<FeedChange> catalogue = ManagedEventDeclarations.ToCatalogue(quads.Select(q => (q.S, q.P, q.O)), []);
        ShapesGraph shapes = ShapesGraph.Load(Trig.Load([.. Trig.Quads(EventDeclarationShapes.Turtle).Select(q => q with { G = null })]), GraphPattern.DefaultGraph, cancellationToken: TestContext.Current.CancellationToken).Shapes!;

        // The old home: each catalogue graph on its own, as ADR 0157 validates one.
        foreach (IGrouping<RdfTerm?, FeedChange> graph in catalogue.GroupBy(c => c.Graph))
        {
            ValidationOutcome old = ShaclValidator.Validate(shapes, Trig.Load(graph.Select(c => new Q(c.Subject, c.Predicate, c.Object, null))), GraphPattern.DefaultGraph, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(old.Succeeded && old.Report!.Conforms, string.Join("; ", old.Problems));
        }

        // The new home: the event nodes, with varve:sourceGraph, beside the dataset's other declarations.
        ValidationOutcome moved = ShaclValidator.Validate(shapes, Trig.Load(quads.Select(q => q with { G = null })), GraphPattern.DefaultGraph, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(moved.Succeeded, string.Join("; ", moved.Problems));
        Assert.True(moved.Report!.Conforms, string.Join("\n", moved.Report.Results.Select(r => r.ToString())));
    }
}
