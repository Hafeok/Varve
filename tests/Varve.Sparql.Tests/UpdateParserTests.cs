// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Xunit;

namespace Varve.Sparql.Tests;

/// <summary>The update operations as a record of the request (<c>docs/spec/sparql-algebra.md</c> §5), and the blank node rules of §19.6.</summary>
public class UpdateParserTests
{
    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example/" + local));

    private static TermPattern T(string local) => new(Iri(local));

    private static Update Parse(string text) => SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes("PREFIX : <http://example/>\n" + text));

    private static SparqlParseError Error(string text)
    {
        Assert.False(SparqlParser.TryParseUpdate(Encoding.UTF8.GetBytes("PREFIX : <http://example/>\n" + text), default, out _, out SparqlParseError error));
        return error;
    }

    [Fact]
    public void operations_are_recorded_in_order_with_their_flags()
    {
        Update update = Parse("LOAD SILENT :src INTO GRAPH :g ; CLEAR ALL ; DROP SILENT GRAPH :g ; CREATE GRAPH :h ; ADD DEFAULT TO :h ; MOVE GRAPH :h TO DEFAULT ; COPY SILENT :h TO :g");

        Assert.Equal(7, update.Operations.Count);
        Assert.Equal(new Load(Iri("src"), Iri("g"), true), update.Operations[0]);
        Assert.Equal(new Clear(GraphTarget.All, false), update.Operations[1]);
        Assert.Equal(new Drop(GraphTarget.Of(Iri("g")), true), update.Operations[2]);
        Assert.Equal(new Create(Iri("h"), false), update.Operations[3]);
        Assert.Equal(new Add(GraphOrDefault.Default, GraphOrDefault.Of(Iri("h")), false), update.Operations[4]);
        Assert.Equal(new Move(GraphOrDefault.Of(Iri("h")), GraphOrDefault.Default, false), update.Operations[5]);
        Assert.Equal(new Copy(GraphOrDefault.Of(Iri("h")), GraphOrDefault.Of(Iri("g")), true), update.Operations[6]);
    }

    [Fact]
    public void insert_data_keeps_graphs_and_blank_nodes_and_refuses_variables()
    {
        Update update = Parse("INSERT DATA { :s :p :o . GRAPH :g { _:b :q 1 } :t :p :o }");
        InsertData data = Assert.IsType<InsertData>(update.Operations[0]);

        Assert.Equal(3, data.Quads.Count);
        Assert.Null(data.Quads[0].Graph);
        Assert.Equal(T("g"), data.Quads[1].Graph);
        Assert.IsType<BlankNodePattern>(data.Quads[1].Subject);
        Assert.Null(data.Quads[2].Graph);

        Assert.Equal(SparqlErrorKind.Syntax, Error("INSERT DATA { ?s :p :o }").Kind);
        Assert.Equal(SparqlErrorKind.Syntax, Error("INSERT DATA { GRAPH ?g { :s :p :o } }").Kind);
        Assert.Equal(SparqlErrorKind.BlankNode, Error("DELETE DATA { _:a :p :o }").Kind);
        Assert.Equal(SparqlErrorKind.BlankNode, Error("DELETE WHERE { _:a :p ?o }").Kind);
        Assert.Equal(SparqlErrorKind.BlankNode, Error("DELETE { ?s :p [] } WHERE { ?s :p ?o }").Kind);
    }

    [Fact]
    public void modify_keeps_with_using_and_the_translated_where()
    {
        Update update = Parse("WITH :g DELETE { ?s :p ?o } INSERT { GRAPH ?h { ?s :q ?o } } USING :a USING NAMED :b WHERE { ?s :p ?o OPTIONAL { ?s :r ?h } }");
        Modify modify = Assert.IsType<Modify>(update.Operations[0]);

        Assert.Equal(Iri("g"), modify.With);
        Assert.Equal(1, modify.Delete.Count);
        Assert.Equal(1, modify.Insert.Count);
        Assert.Equal(new VariablePattern(new Variable("h")), modify.Insert[0].Graph);
        Assert.Equal(new DatasetSpec(AlgebraList.Of(Iri("a")), AlgebraList.Of(Iri("b"))), modify.Using);
        Assert.IsType<LeftJoin>(modify.Where);

        Update insertOnly = Parse("INSERT { ?s :q ?o } WHERE { ?s :p ?o }");
        Modify only = Assert.IsType<Modify>(insertOnly.Operations[0]);
        Assert.Null(only.With);
        Assert.True(only.Delete.IsEmpty);
        Assert.Null(only.Using);
    }

    [Fact]
    public void prologues_may_follow_a_semicolon_and_the_last_operation_may_be_missing()
    {
        Update update = Parse("CLEAR DEFAULT ; PREFIX ex: <http://other/> INSERT DATA { ex:s ex:p ex:o } ;");
        Assert.Equal(2, update.Operations.Count);
        Assert.Equal(2, update.Prologue.Prefixes.Count);
        Assert.Equal(RdfTerm.Iri("http://other/s"u8), Assert.IsType<TermPattern>(Assert.IsType<InsertData>(update.Operations[1]).Quads[0].Subject).Term);

        Assert.True(Parse("# nothing but a prefix").Operations.IsEmpty);
        Assert.Equal(SparqlErrorKind.Syntax, Error("CREATE GRAPH :g ;; LOAD :r").Kind);
    }

    [Fact]
    public void a_label_may_recur_within_one_insert_data_and_not_across_two()
    {
        Update one = Parse("INSERT DATA { _:a :p :o . GRAPH :g { _:a :q :o } }");
        Assert.Equal(2, Assert.IsType<InsertData>(one.Operations[0]).Quads.Count);

        Assert.Equal(SparqlErrorKind.BlankNode, Error("INSERT DATA { _:a :p :o } ; INSERT DATA { _:a :q :o }").Kind);
        Assert.Equal(SparqlErrorKind.BlankNode, Error("INSERT { ?s :p ?o } WHERE { _:a :p ?o } ; INSERT { ?s :p ?o } WHERE { _:a :q ?o }").Kind);

        // A template shares labels with its own WHERE clause.
        Update shared = Parse("INSERT { _:a :p ?o } WHERE { _:a :q ?o }");
        Assert.IsType<Modify>(shared.Operations[0]);
    }
}
