// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Management.Model;
using Varve.Rdf;
using Xunit;

namespace Varve.Management.Tests;

public sealed class DeclarationTests
{
    /// <summary>The plan's example dataset, without its event type, which part 2 brings.</summary>
    internal const string Notes = """
        <https://w3id.org/varve/ns#manage/dataset/notes> {
          n:notes a varve:Dataset ;
            varve:datasetName "notes" ;
            varve:storage varve:ObjectStorage ;
            varve:defaultAccessScope varve:AllHistory ;
            varve:placedOn "varve-1" ;
            varve:automaticHandover false .

          n:users a varve:Role ; varve:claimValue "Notes.User" .
          n:support a varve:Group ; varve:claimType "groups" ; varve:claimValue "support-team" .

          n:own-graphs a varve:GraphTemplate ;
            varve:template "https://notes.example/users/{sub}/" ;
            varve:match varve:Prefix .
          n:users-own a varve:Grant ;
            varve:grantee n:users ; varve:permission varve:write ; varve:graphTemplate n:own-graphs .
          n:users-shared a varve:Grant ;
            varve:grantee n:users ; varve:permission varve:read ;
            varve:graph <https://notes.example/shared> .
          n:support-read a varve:Grant ;
            varve:grantee n:support ; varve:permission varve:read ;
            varve:graphPrefix "https://notes.example/users/" .
          n:owner-admin a varve:Grant ;
            varve:grantee <https://login.example/v2.0#owner> ;
            varve:permission varve:admin .
          n:shapes a varve:ShapesBinding ; varve:boundGraph <https://notes.example/shapes> ;
            varve:validationMode varve:Reject ; varve:graphPrefix "https://notes.example/users/" ;
            varve:subClassOfInShapesGraph true .
        }

        <https://w3id.org/varve/ns#manage/fleet> {
          <https://ops.example/manage/platform> a varve:Role ; varve:claimValue "Varve.Operator" .
          <https://ops.example/manage/ops> a varve:Grant ;
            varve:grantee <https://ops.example/manage/platform> ; varve:permission varve:operator .
        }
        """;

    /// <summary>
    /// The writer set (ADR 0180): the preferred writer and the standbys, which
    /// need the opt-in and are never the preferred writer.
    /// </summary>
    [Fact]
    public void standbys_read_as_the_writer_set_and_need_the_opt_in()
    {
        const string Set = """
            <https://w3id.org/varve/ns#manage/dataset/notes> {
              n:notes a varve:Dataset ; varve:datasetName "notes" ; varve:storage varve:ObjectStorage ;
                varve:placedOn "varve-0" ; varve:standbyOn "varve-2" , "varve-1" ; varve:automaticHandover true .
            }
            """;
        Declarations set = T.Read(Set);
        Assert.Empty(set.Findings);
        DatasetDeclaration notes = Assert.Single(set.Datasets.Values);
        Assert.Equal("varve-0", notes.PlacedOn);
        Assert.Equal(["varve-1", "varve-2"], notes.StandbyOn);
        Assert.True(notes.AutomaticHandover);

        Assert.NotEmpty(T.Read(Set.Replace("varve:automaticHandover true", "varve:automaticHandover false", System.StringComparison.Ordinal)).Findings);
        Assert.NotEmpty(T.Read(Set.Replace("\"varve-2\"", "\"varve-0\"", System.StringComparison.Ordinal)).Findings);
    }

    [Fact]
    public void the_plans_example_reads_whole_with_no_finding()
    {
        Declarations declarations = T.Read(Notes);

        Assert.Empty(declarations.Findings);
        DatasetDeclaration notes = Assert.Single(declarations.Datasets.Values);
        Assert.Equal("notes", notes.Name);
        Assert.Equal(StorageKind.ObjectStorage, notes.Storage);
        Assert.Equal(HistoryScope.AllHistory, notes.DefaultAccessScope);
        Assert.Equal("varve-1", notes.PlacedOn);
        Assert.False(notes.AutomaticHandover);
        Assert.Equal(4, notes.Grants.Count);
        BindingDeclaration binding = Assert.Single(notes.Bindings);
        Assert.Equal(BindingKind.Shapes, binding.Kind);
        Assert.Equal(ShapesMode.Reject, binding.Mode);
        Assert.Equal(["https://notes.example/users/"], binding.Validates!.Prefixes);
        Assert.True(binding.SubClassOfInShapesGraph);
        Assert.False(binding.AllowFullEvaluation);
        GrantDeclaration ops = Assert.Single(declarations.FleetGrants);
        Assert.Equal(GrantAccess.Operator, ops.Access);
        Assert.Equal(Principal.Role("Varve.Operator"), ops.Grantee);
    }

    /// <summary>
    /// A user reads and writes their own graphs through the template, also when
    /// an application calls on their behalf; reads the shared graph; and sees
    /// no other user's. Support reads every user's graphs and writes none.
    /// </summary>
    [Fact]
    public void grants_give_each_caller_its_graphs()
    {
        GrantTable table = GrantTable.Compile(T.Read(Notes), new ManagementPosition(7), []);

        CallerScope alice = table.ScopesOf(T.Caller(agent: "https://login.example/v2.0#alice", sub: "alice", roles: ["Notes.User"]), "notes");
        Assert.False(alice.IsAdmin);
        Assert.True(alice.Readable.Allows(T.Iri("https://notes.example/users/alice/todo")));
        Assert.True(alice.Writable.Allows(T.Iri("https://notes.example/users/alice/todo")));
        Assert.True(alice.Readable.Allows(T.Iri("https://notes.example/shared")));
        Assert.False(alice.Writable.Allows(T.Iri("https://notes.example/shared")));
        Assert.False(alice.Readable.Allows(T.Iri("https://notes.example/users/bob/todo")));
        Assert.False(alice.Readable.Allows(null));

        CallerScope support = table.ScopesOf(T.Caller(sub: "s", claims: [("groups", "support-team")]), "notes");
        Assert.True(support.Readable.Allows(T.Iri("https://notes.example/users/bob/todo")));
        Assert.False(support.Writable.Allows(T.Iri("https://notes.example/users/bob/todo")));

        Assert.True(table.ScopesOf(T.Caller(agent: "https://login.example/v2.0#owner"), "notes").IsAdmin);
        Assert.True(table.IsOperator(T.Caller(roles: ["Varve.Operator"])));
        Assert.True(table.ScopesOf(T.Caller(roles: ["Varve.Operator"]), "notes").IsAdmin);
        Assert.False(table.IsOperator(T.Caller(roles: ["Notes.User"])));

        CallerScope stranger = table.ScopesOf(T.Caller(sub: "x", roles: ["Other"]), "notes");
        Assert.False(stranger.Readable.Allows(T.Iri("https://notes.example/shared")));
        Assert.False(table.Reaches(T.Caller(sub: "x", roles: ["Other"]), "notes"));
        Assert.False(table.ScopesOf(T.Caller(sub: "alice", roles: ["Notes.User"]), "absent").Readable.Allows(T.Iri("https://notes.example/shared")));
    }

    [Fact]
    public void a_bootstrap_operator_is_an_agent_iri_or_a_role_value()
    {
        GrantTable table = GrantTable.Compile(Declarations.Empty, default, ["https://login.example/v2.0#root", "Varve.Root"]);

        Assert.True(table.IsOperator(T.Caller(agent: "https://login.example/v2.0#root")));
        Assert.True(table.IsOperator(T.Caller(roles: ["Varve.Root"])));
        Assert.False(table.IsOperator(T.Caller(roles: ["https://login.example/v2.0#root"])));
        Assert.False(table.IsOperator(T.Caller(agent: "Varve.Root")));
    }

    [Theory]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . _:b a varve:Grant .", "blank node")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"other\" .", "must be equal")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" ; varve:colour \"red\" .", "varve:colour is not a term")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:t a varve:EventType .", "ADR 0167")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" ; varve:eventDeclarations varve:Moving . n:t a varve:EventType .", "varve:eventDeclarations varve:Moved")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" ; varve:eventDeclarations varve:read .", "varve:Moving or varve:Moved")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" ; varve:eventDeclarations varve:Moving, varve:Moved .", "at most once")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:x varve:eventDeclarations varve:Moved .", "dataset's node only")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" ; varve:erasureMode true .", "#14")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:c a varve:ClassifierBinding .", "#14")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:operator .", "fleet graph only")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:admin ; varve:allGraphs true .", "names no graph set")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:read .", "names its graphs")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:g a varve:Grant ; varve:grantee <https://i#a>, <https://i#b> ; varve:permission varve:read ; varve:allGraphs true .", "exactly one varve:grantee")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:read ; varve:graphTemplate n:t . n:t a varve:GraphTemplate ; varve:template \"https://x/{user}/\" ; varve:match varve:Prefix .", "{user}")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:g a varve:Grant ; varve:grantee <https://i#a> ; varve:permission varve:read ; varve:graph \"not an iri\" .", "absolute IRI")]
    [InlineData("", "exactly one varve:Dataset")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:b a varve:ShapesBinding ; varve:boundGraph <https://x/shapes> ; varve:allGraphs true .", "exactly one varve:validationMode")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:b a varve:ShapesBinding ; varve:boundGraph <https://x/shapes> ; varve:validationMode varve:read ; varve:allGraphs true .", "exactly one varve:validationMode")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:b a varve:ShapesBinding ; varve:boundGraph <https://x/shapes> ; varve:validationMode varve:Report .", "a shapes binding names its graphs")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:b a varve:ShapesBinding ; varve:boundGraph <https://x/shapes> ; varve:validationMode varve:Report ; varve:graph <https://x/shapes> .", "own shapes graph")]
    [InlineData("n:d a varve:Dataset ; varve:datasetName \"d\" . n:b a varve:ShapesBinding ; varve:boundGraph <https://x/shapes> ; varve:validationMode varve:Report ; varve:graphTemplate n:t . n:t a varve:GraphTemplate ; varve:template \"https://x/{sub}/\" ; varve:match varve:Prefix .", "without varve:graphTemplate")]
    public void the_code_validator_names_what_is_wrong(string body, string expected)
    {
        Declarations declarations = T.Read("<https://w3id.org/varve/ns#manage/dataset/d> { n:x n:y n:z . " + body + " }");

        Assert.Contains(declarations.Findings, finding => finding.Message.Contains(expected, System.StringComparison.Ordinal));
        Assert.All(declarations.Findings, finding => Assert.Equal(T.Iri("https://w3id.org/varve/ns#manage/dataset/d"), finding.Graph));
    }

    [Fact]
    public void a_graph_outside_the_management_graphs_and_the_default_graph_are_findings()
    {
        Declarations declarations = T.Read("<https://example.org/elsewhere> { n:a n:b n:c . } n:a n:b n:c .");

        Assert.Equal(2, declarations.Findings.Count);
        Assert.Contains(declarations.Findings, finding => finding.Message.Contains("default graph", System.StringComparison.Ordinal));
        Assert.Contains(declarations.Findings, finding => finding.Message.Contains("varve:manage/fleet", System.StringComparison.Ordinal));
    }

    [Fact]
    public void the_bootstrap_graph_holds_only_operators()
    {
        Declarations declarations = T.Read("<https://w3id.org/varve/ns#manage/bootstrap> { <https://w3id.org/varve/ns#manage/bootstrap> varve:bootstrapOperator \"Varve.Root\", \"https://i#root\" . }");

        Assert.Empty(declarations.Findings);
        Assert.Equal(["Varve.Root", "https://i#root"], declarations.Bootstrap);
        Assert.Contains(T.Read("<https://w3id.org/varve/ns#manage/fleet> { n:x varve:bootstrapOperator \"me\" . }").Findings, f => f.Message.Contains("varve:bootstrapOperator", System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://notes.example/users/{sub}/", "a b/é", "https://notes.example/users/a%20b%2F%C3%A9/")]
    [InlineData("https://t.example/{claim.tid}/{sub}", "u", "https://t.example/contoso/u")]
    public void a_template_expands_percent_encoded(string template, string sub, string expected)
    {
        Caller caller = T.Caller(agent: "https://i#" + sub, sub: sub, claims: [("tid", "contoso")]);

        Assert.Null(GraphTemplates.Check(template));
        Assert.Equal(expected, GraphTemplates.Expand(new GraphTemplate(template, TemplateMatch.Exact), caller));
    }

    [Fact]
    public void a_caller_without_the_variable_gets_nothing()
    {
        Assert.Null(GraphTemplates.Expand(new GraphTemplate("https://t.example/{claim.tid}/", TemplateMatch.Prefix), T.Caller(sub: "u")));
        Assert.Null(GraphTemplates.Expand(new GraphTemplate("https://t.example/{claim.tid}/", TemplateMatch.Prefix), T.Caller(sub: "u", claims: [("tid", "a"), ("tid", "b")])));
        Assert.NotNull(GraphTemplates.Check("{sub}"));
        Assert.NotNull(GraphTemplates.Check("https://x/{sub"));
    }

    [Fact]
    public void the_vocabulary_names_the_graphs()
    {
        Assert.Equal(T.Iri("https://w3id.org/varve/ns#manage/dataset/notes"), ManagementVocabulary.DatasetGraph("notes"));
        Assert.True(ManagementVocabulary.TryGetDataset(ManagementVocabulary.DatasetGraph("a.b-c_1"), out string? name));
        Assert.Equal("a.b-c_1", name);
        Assert.False(ManagementVocabulary.TryGetDataset(T.Iri("https://w3id.org/varve/ns#manage/dataset/_x"), out _));
        Assert.True(ManagementVocabulary.IsManagementGraph(ManagementVocabulary.FleetGraph));
        Assert.False(ManagementVocabulary.IsManagementGraph(T.Iri("https://w3id.org/varve/ns#setup")));
        Assert.DoesNotContain(T.Read(Notes).Datasets.Keys, key => key != "notes");
    }

    [Fact]
    public void a_moved_graph_holds_event_declarations_and_says_where_they_are()
    {
        Declarations declarations = T.Read("""
            <https://w3id.org/varve/ns#manage/dataset/d> {
              n:d a varve:Dataset ; varve:datasetName "d" ; varve:eventDeclarations varve:Moved .
              n:t a varve:EventType ; varve:sourceGraph varve:defaultGraph ; varve:eventName "T" ; varve:eventField n:f .
              n:f a varve:EventField ; varve:fieldName "f" .
            }
            """);

        Assert.Empty(declarations.Findings);
        Assert.Equal(EventDeclarationState.Moved, declarations.Datasets["d"].EventDeclarations);
    }

    [Fact]
    public void event_terms_stay_refused_in_the_fleet_graph()
    {
        Declarations declarations = T.Read("""
            <https://w3id.org/varve/ns#manage/fleet> { n:t a varve:EventType ; varve:eventDeclarations varve:Moved . }
            """);

        Assert.Contains(declarations.Findings, finding => finding.Message.Contains("ADR 0167", System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(EventDeclarationState.InDataset, EventDeclarationState.InDataset, true)]
    [InlineData(EventDeclarationState.InDataset, EventDeclarationState.Moving, true)]
    [InlineData(EventDeclarationState.Moving, EventDeclarationState.InDataset, true)]
    [InlineData(EventDeclarationState.Moving, EventDeclarationState.Moved, true)]
    [InlineData(EventDeclarationState.Moved, EventDeclarationState.Moved, true)]
    [InlineData(EventDeclarationState.InDataset, EventDeclarationState.Moved, false)]
    [InlineData(EventDeclarationState.Moved, EventDeclarationState.Moving, false)]
    [InlineData(EventDeclarationState.Moved, EventDeclarationState.InDataset, false)]
    public void the_move_s_transitions(EventDeclarationState from, EventDeclarationState to, bool allowed)
    {
        Assert.Equal(allowed, EventDeclarationMoves.Allowed(from, to));
        Declarations before = T.Read(Graph(from));
        Declarations after = T.Read(Graph(to));
        Assert.Equal(allowed, EventDeclarationMoves.Check(before, after).Count == 0);

        static string Graph(EventDeclarationState state) =>
            "<https://w3id.org/varve/ns#manage/dataset/d> { n:d a varve:Dataset ; varve:datasetName \"d\""
            + state switch { EventDeclarationState.Moving => " ; varve:eventDeclarations varve:Moving", EventDeclarationState.Moved => " ; varve:eventDeclarations varve:Moved", _ => string.Empty }
            + " . }";
    }

    [Fact]
    public void a_cancel_states_varve_InDataset_and_the_dataset_may_move_again_from_it()
    {
        const string Moving = "<https://w3id.org/varve/ns#manage/dataset/d> { n:d a varve:Dataset ; varve:datasetName \"d\" ; varve:eventDeclarations varve:Moving . }";
        const string Cancelled = "<https://w3id.org/varve/ns#manage/dataset/d> { n:d a varve:Dataset ; varve:datasetName \"d\" ; varve:eventDeclarations varve:InDataset . }";
        Declarations cancelled = T.Read(Cancelled);
        Assert.Empty(cancelled.Findings);
        Assert.Equal(EventDeclarationState.InDataset, cancelled.Datasets["d"].EventDeclarations);
        Assert.Empty(EventDeclarationMoves.Check(T.Read(Moving), cancelled));
        Assert.Empty(EventDeclarationMoves.Check(cancelled, T.Read(Moving)));
    }

    [Fact]
    public void a_deleted_dataset_is_not_a_transition()
    {
        Declarations before = T.Read("<https://w3id.org/varve/ns#manage/dataset/d> { n:d a varve:Dataset ; varve:datasetName \"d\" ; varve:eventDeclarations varve:Moved . }");
        Assert.Empty(EventDeclarationMoves.Check(before, Declarations.Empty));
    }
}
