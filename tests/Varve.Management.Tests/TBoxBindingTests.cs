// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using CsCheck;
using Varve.Management.Model;
using Varve.Rdf;
using Xunit;

namespace Varve.Management.Tests;

/// <summary>
/// The TBox binding (ADR 0194): how it reads, what it makes dataset-public in
/// the grant table (ADR 0190 point 3), and the plan's list of the graphs an
/// apply widens and narrows (ADR 0190 point 5).
/// </summary>
public sealed class TBoxBindingTests
{
    // Sized for the land gate: each case reads and plans two small dataset graphs twice.
    private const int Iterations = 300;

    private const string Notes = """
        <https://w3id.org/varve/ns#manage/dataset/notes> {
          n:notes a varve:Dataset ; varve:datasetName "notes" .
          n:alice a varve:Grant ; varve:grantee <https://login.example/v2.0#alice> ; varve:permission varve:read ; varve:graphPrefix "https://notes.example/users/alice/" .
          n:bob a varve:Grant ; varve:grantee <https://login.example/v2.0#bob> ; varve:permission varve:write ; varve:graph <https://notes.example/schema> .
          n:schema a varve:TBoxBinding ;
            varve:boundGraph <https://notes.example/schema> , <https://notes.example/extra> ;
            varve:profile varve:Rewriting ;
            varve:graphPrefix "https://notes.example/users/" ;
            varve:defaultEntailment <http://www.w3.org/ns/entailment/RDFS> .
        }
        """;

    [Fact]
    public void A_binding_reads_into_its_TBox_graphs_data_graphs_and_default()
    {
        Declarations declarations = T.Read(Notes);

        Assert.Empty(declarations.Findings);
        DatasetDeclaration notes = declarations.Datasets["notes"];
        Assert.Empty(notes.Bindings);
        TBoxBindingDeclaration binding = Assert.Single(notes.TBoxBindings);
        Assert.Equal([T.Iri("https://notes.example/extra"), T.Iri("https://notes.example/schema")], binding.TBoxGraphs);
        Assert.Equal(["https://notes.example/users/"], binding.DataGraphs.Prefixes);
        Assert.Equal(TBoxProfile.Rewriting, binding.Profile);
        Assert.Equal(T.Iri("http://www.w3.org/ns/entailment/RDFS"), notes.DefaultEntailment);
    }

    [Theory]
    [InlineData("varve:profile varve:Rewriting", "varve:profile varve:RDFS", "#171")]
    [InlineData("varve:profile varve:Rewriting ;", "varve:profile varve:Rewriting ; varve:crossGraph true ;", "#171")]
    [InlineData("varve:profile varve:Rewriting ;", "varve:profile varve:Rewriting ; varve:sameAs varve:SameAsOff ;", "#95")]
    [InlineData("varve:profile varve:Rewriting ;", "varve:profile varve:Rewriting ; varve:inconsistencyMode varve:Reject ;", "#95")]
    public void A_reserved_term_is_refused_naming_the_part_that_gives_it_effect(string from, string to, string part)
    {
        Declarations declarations = T.Read(Notes.Replace(from, to, System.StringComparison.Ordinal));

        Assert.NotEmpty(declarations.Findings);
        Assert.All(declarations.Findings, finding => Assert.Contains(part, finding.Message, System.StringComparison.Ordinal));
        Assert.Empty(declarations.Datasets);
    }

    /// <summary>
    /// A TBox graph is read by every caller who reads anything in the
    /// dataset, and written only by a write grant on it; a caller who reads
    /// nothing there reads none of it (ADR 0190 point 3).
    /// </summary>
    [Fact]
    public void A_TBox_graph_is_read_by_every_reader_of_the_dataset_and_written_by_its_grant()
    {
        GrantTable table = GrantTable.Compile(T.Read(Notes), new ManagementPosition(1), []);
        RdfTerm schema = T.Iri("https://notes.example/schema");
        RdfTerm extra = T.Iri("https://notes.example/extra");

        CallerScope alice = table.ScopesOf(T.Caller(agent: "https://login.example/v2.0#alice"), "notes");
        Assert.True(alice.Readable.Allows(schema));
        Assert.True(alice.Readable.Allows(extra));
        Assert.False(alice.Writable.Allows(schema));
        Assert.False(alice.Readable.Allows(T.Iri("https://notes.example/users/bob/g")));

        CallerScope bob = table.ScopesOf(T.Caller(agent: "https://login.example/v2.0#bob"), "notes");
        Assert.True(bob.Writable.Allows(schema));
        Assert.False(bob.Writable.Allows(extra));

        CallerScope stranger = table.ScopesOf(T.Caller(agent: "https://login.example/v2.0#mallory"), "notes");
        Assert.False(stranger.Readable.Allows(schema));
        Assert.True(stranger.Readable.IsEmpty);
    }

    private static readonly string[] Datasets = ["a", "b"];

    private static readonly string[] Pool = ["https://x/tbox0", "https://x/tbox1", "https://x/tbox2", "https://x/tbox3"];

    // A dataset graph: absent, or a dataset with up to two bindings over disjoint prefixes, each binding a non-empty subset of the pool.
    private static readonly Gen<int[]?> DatasetGraph =
        Gen.Frequency((1, Gen.Const((int[]?)null)), (4, Gen.Int[1, 15].Array[0, 2].Select(masks => (int[]?)masks)));

    /// <summary>
    /// ADR 0194 point 6's property: for generated current and desired
    /// declarations, the plan's widened and narrowed graphs are exactly those
    /// whose dataset-public status the apply changes, as a reader of the
    /// dataset's data graphs sees it in the grant table, and its unchanged
    /// ones are those dataset-public on both sides.
    /// </summary>
    [Fact]
    public void The_plan_names_exactly_the_graphs_the_apply_widens_and_narrows()
    {
        Gen.Select(DatasetGraph, DatasetGraph, DatasetGraph, DatasetGraph).Sample((ca, cb, da, db) =>
        {
            InMemoryDataset current = T.Trig(Wrap("a", ca) + Wrap("b", cb));
            InMemoryDataset desired = T.Trig(Wrap("a", da) + Wrap("b", db));
            IReadOnlyList<RdfTerm> owned = [ManagementVocabulary.DatasetGraph("a"), ManagementVocabulary.DatasetGraph("b")];
            ManagementPlan plan = ManagementPlan.Compute(desired, owned, current, new Dictionary<RdfTerm, ManagementPosition>(), new ManagementPosition(3));

            GrantTable before = GrantTable.Compile(DeclarationReader.Read(current), new ManagementPosition(3), []);
            GrantTable after = GrantTable.Compile(DeclarationReader.Read(desired), new ManagementPosition(4), []);
            Caller reader = T.Caller(agent: "https://login.example/v2.0#reader");

            foreach (string dataset in Datasets)
            {
                foreach (string iri in Pool)
                {
                    RdfTerm graph = T.Iri(iri);
                    bool was = before.ScopesOf(reader, dataset).Readable.Allows(graph);
                    bool now = after.ScopesOf(reader, dataset).Readable.Allows(graph);
                    DatasetPublicGraph[] listed = [.. plan.DatasetPublic.Where(p => p.Dataset == dataset && p.Graph.Equals(graph))];
                    string shown = dataset + " <" + iri + "> was " + was + ", is " + now + ", listed " + string.Join(", ", listed.Select(p => p.Change));

                    if (!was && !now)
                    {
                        Assert.True(listed.Length == 0, shown);
                        continue;
                    }

                    DatasetPublicGraph one = Assert.Single(listed);
                    Assert.True(one.Change == (was && now ? DatasetPublicChange.Unchanged : now ? DatasetPublicChange.Widened : DatasetPublicChange.Narrowed), shown);
                }
            }

            Assert.All(plan.DatasetPublic, p => Assert.Contains(Encoding.UTF8.GetString(p.Graph.Lexical), Pool));
            ManagementPlan back = ManagementPlan.FromJson(plan.ToJson());
            Assert.Equal(plan.DatasetPublic, back.DatasetPublic);
            foreach (DatasetPublicGraph widened in plan.DatasetPublic.Where(p => p.Change == DatasetPublicChange.Widened))
            {
                Assert.Contains(widened.Dataset + " <" + Encoding.UTF8.GetString(widened.Graph.Lexical) + "> (widened", plan.Render(), System.StringComparison.Ordinal);
            }
        }, iter: Iterations);
    }

    private static string Wrap(string dataset, int[]? bindings)
    {
        if (bindings is null)
        {
            return string.Empty;
        }

        StringBuilder text = new();
        text.Append("<https://w3id.org/varve/ns#manage/dataset/").Append(dataset).Append("> {\n");
        text.Append("n:").Append(dataset).Append(" a varve:Dataset ; varve:datasetName \"").Append(dataset).Append("\" .\n");
        text.Append("n:reader-").Append(dataset).Append(" a varve:Grant ; varve:grantee <https://login.example/v2.0#reader> ; varve:permission varve:read ; varve:graphPrefix \"https://x/d\" .\n");

        for (int i = 0; i < bindings.Length; i++)
        {
            IEnumerable<string> graphs = Pool.Where((_, bit) => (bindings[i] & (1 << bit)) != 0).Select(iri => "<" + iri + ">");
            text.Append("n:tbox-").Append(dataset).Append(i).Append(" a varve:TBoxBinding ; varve:boundGraph ").Append(string.Join(" , ", graphs));
            text.Append(" ; varve:profile varve:Rewriting ; varve:graphPrefix \"https://x/d").Append(i).Append("/\" .\n");
        }

        return text.Append("}\n").ToString();
    }
}
