// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using Varve.Management.Model;
using Varve.Rdf;
using Xunit;

namespace Varve.Management.Tests;

/// <summary>
/// ADR 0234: the classifier binding and subject-scoped grants are read, their
/// preconditions refused by name, and the grant table answers a row scope per
/// binding, templates and links expanded for the caller.
/// </summary>
public class RowDeclarationTests
{
    private const string Graph = "<https://w3id.org/varve/ns#manage/dataset/d>";
    private const string Dataset = "n:d a varve:Dataset ; varve:datasetName \"d\" . ";
    private const string Binding = "n:c a varve:ClassifierBinding ; varve:boundGraph <https://x/shapes> ; varve:graphPrefix \"https://x/people/\" . ";

    [Fact]
    public void a_classifier_binding_and_a_subject_scoped_grant_are_read()
    {
        Declarations declarations = T.Read(Graph + " { " + Dataset + Binding
            + "n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:write ; varve:allGraphs true ; "
            + "varve:subject <https://x/alice> ; varve:subjectPrefix \"https://x/tenant/1/\" ; varve:subjectClass <https://x/Client> ; "
            + "varve:subjectTemplate n:t ; varve:subjectLink n:l ; varve:createUnder \"https://x/new/\" . "
            + "n:t a varve:SubjectTemplate ; varve:template \"https://x/people/{sub}\" ; varve:match varve:Exact . "
            + "n:l a varve:SubjectLink ; varve:linkPredicate <https://x/caseworker> ; varve:linkTarget varve:CallerAgent . }");

        Assert.Empty(declarations.Findings);
        DatasetDeclaration d = declarations.Datasets["d"];
        BindingDeclaration binding = Assert.Single(d.Bindings);
        Assert.Equal(BindingKind.Classifier, binding.Kind);
        Assert.Equal(["https://x/people/"], binding.Classifies!.Prefixes);
        SubjectSet subjects = Assert.Single(d.Grants).Subjects;
        Assert.False(subjects.All);
        Assert.Equal([T.Iri("https://x/alice")], subjects.Subjects);
        Assert.Equal(["https://x/tenant/1/"], subjects.Prefixes);
        Assert.Equal([T.Iri("https://x/Client")], subjects.Classes);
        Assert.Null(Assert.Single(subjects.Links).Target);
        Assert.Equal(["https://x/new/"], subjects.CreateUnder);

        GrantTable table = GrantTable.Compile(declarations, default, []);
        CallerScope scope = table.ScopesOf(T.Caller(agent: "https://i#a", sub: "bob"), "d");
        SubjectScope rows = scope.ReadableRows.For(T.Iri("https://notes.example/manage/c"));
        Assert.False(rows.IsAll);
        Assert.True(rows.AllowsByName(T.Iri("https://x/alice")));
        Assert.True(rows.AllowsByName(T.Iri("https://x/people/bob")));
        Assert.True(rows.AllowsByName(T.Iri("https://x/tenant/1/z")));
        Assert.False(rows.AllowsByName(T.Iri("https://x/people/carol")));
        Assert.Equal(T.Iri("https://i#a"), Assert.Single(rows.Links).Target);
        Assert.True(scope.WritableRows.For(T.Iri("https://notes.example/manage/c")).MayCreate(T.Iri("https://x/new/1")));
    }

    [Fact]
    public void a_grant_without_subject_terms_reads_every_row_and_one_with_them_restricts_only_its_bindings()
    {
        Declarations declarations = T.Read(Graph + " { " + Dataset + Binding
            + "n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:read ; varve:allGraphs true ; varve:subject <https://x/alice> . "
            + "n:h a varve:Grant ; varve:grantee <https://i#b> ; varve:permission varve:read ; varve:allGraphs true . }");

        Assert.Empty(declarations.Findings);
        GrantTable table = GrantTable.Compile(declarations, default, []);
        Assert.False(table.ScopesOf(T.Caller(agent: "https://i#a"), "d").ReadableRows.IsAll);
        Assert.True(table.ScopesOf(T.Caller(agent: "https://i#b"), "d").ReadableRows.IsAll);
        Assert.True(table.ScopesOf(T.Caller(agent: "https://i#a"), "d").WritableRows.For(T.Iri("https://notes.example/manage/c")).Subjects.Count == 0);
    }

    [Theory]
    [InlineData("n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:read ; varve:graph <https://x/people/1> ; varve:subject <https://x/a> .", "P1")]
    [InlineData("n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:read ; varve:graph <https://x/other> ; varve:subject <https://x/a> .", "P3")]
    [InlineData("n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:read ; varve:graphTemplate n:t ; varve:subject <https://x/a> . n:t a varve:GraphTemplate ; varve:template \"https://x/people/{sub}\" ; varve:match varve:Prefix .", "P2")]
    [InlineData("n:k a varve:ClassifierBinding ; varve:boundGraph <https://x/other-shapes> ; varve:graph <https://x/people/2> .", "P4")]
    [InlineData("n:s a varve:ShapesBinding ; varve:boundGraph <https://x/s> ; varve:validationMode varve:Reject ; varve:allGraphs true . n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:write ; varve:allGraphs true ; varve:subject <https://x/a> .", "P5")]
    [InlineData("n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:read ; varve:allGraphs true ; varve:createUnder \"https://x/\" .", "on a write grant")]
    [InlineData("n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:admin ; varve:subject <https://x/a> .", "every row")]
    [InlineData("n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:read ; varve:allGraphs true ; varve:subjectLink n:l . n:l a varve:SubjectLink ; varve:linkTarget varve:CallerAgent .", "varve:linkPredicate")]
    [InlineData("n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:read ; varve:allGraphs true ; varve:subjectTemplate n:t . n:t a varve:SubjectTemplate ; varve:template \"https://x/{user}\" ; varve:match varve:Exact .", "{user}")]
    [InlineData("n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:read ; varve:allGraphs true ; varve:subject \"alice\" .", "varve:subject is an absolute IRI")]
    [InlineData("n:b a varve:ClassifierBinding ; varve:boundGraph <https://x/s2> .", "names its graphs")]
    [InlineData("n:b a varve:ClassifierBinding ; varve:boundGraph <https://x/s2> ; varve:graph <https://x/s2> .", "own shapes graph")]
    [InlineData("n:x varve:privacy n:y .", "#203")]
    public void a_row_declaration_that_breaks_a_rule_is_named(string body, string expected)
    {
        Declarations declarations = T.Read(Graph + " { " + Dataset + Binding + body + " }");

        Assert.Contains(declarations.Findings, finding => finding.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void containment_and_meeting_are_set_inclusion_and_intersection()
    {
        GraphSet people = new() { Prefixes = ["https://x/people/"] };
        Assert.True(RowPreconditions.Contains(GraphSet.Everything, people));
        Assert.True(RowPreconditions.Contains(new GraphSet { Prefixes = ["https://x/"] }, people));
        Assert.False(RowPreconditions.Contains(new GraphSet { Graphs = [T.Iri("https://x/people/1")] }, people));
        Assert.True(RowPreconditions.Meets(new GraphSet { Graphs = [T.Iri("https://x/people/1")] }, people));
        Assert.False(RowPreconditions.Meets(new GraphSet { Graphs = [T.Iri("https://x/other")] }, people));
        Assert.True(RowPreconditions.Contains(new GraphSet { DefaultGraph = true, Prefixes = ["https://x/"] }, new GraphSet { DefaultGraph = true, Graphs = [T.Iri("https://x/a")] }));
        Assert.False(RowPreconditions.Contains(people, new GraphSet { DefaultGraph = true }));
        Assert.True(new[] { RowPreconditions.Meets(GraphSet.Everything, new GraphSet()) }.All(static b => !b));
    }
}
