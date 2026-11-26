// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Varve.Benchmarks.Candidates;
using Varve.Inference.Model;
using Varve.Rdf;
using Xunit;
using static Varve.Inference.Tests.Terms;

namespace Varve.Inference.Tests;

/// <summary>
/// ADR 0197, amended 2026-11-24 (#399): the list rules and checks read a list
/// only from asserted statements. A cell is a subject the asserted statements
/// give exactly one <c>rdf:first</c> and one <c>rdf:rest</c>; an inferred one
/// neither makes nor breaks a list; every other subject of an asserted one is
/// reported unread with its reason; and a change to the asserted lists
/// rebuilds the closure, which stays equal to the closure from scratch.
/// </summary>
public class ListsFromAssertedStatementsTests
{
    private const string OwlNs = "http://www.w3.org/2002/07/owl#";

    private static readonly RdfTerm First = IriOf(RdfNs + "first");
    private static readonly RdfTerm Rest = IriOf(RdfNs + "rest");
    private static readonly RdfTerm Nil = IriOf(RdfNs + "nil");
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly RdfTerm SubPropertyOf = IriOf(RdfsNs + "subPropertyOf");
    private static readonly RdfTerm UnionOf = IriOf(OwlNs + "unionOf");
    private static readonly RdfTerm OneOf = IriOf(OwlNs + "oneOf");

    /// <summary>Retraction strategies the closure is held to, the candidates beside DRed among them.</summary>
    public static TheoryData<string> Strategies => new() { "DeleteAndRederive", "RecursiveCounting", "BackwardForward" };

    private static Retraction Strategy(string name) => name switch
    {
        "RecursiveCounting" => RecursiveCounting.Instance,
        "BackwardForward" => BackwardForward.Instance,
        _ => Retraction.DeleteAndRederive,
    };

    private static (Terms Terms, Program Program) Owl()
    {
        Terms terms = new();
        return (terms, Program.Of(InferenceProfile.Owl2Rl, terms));
    }

    private static HashSet<Fact> FromScratch(Program program, IEnumerable<Fact> explicitFacts)
    {
        Closure fresh = new(program, Retraction.FromScratch);
        fresh.Apply([.. explicitFacts], []);
        return [.. fresh.All];
    }

    /// <summary>A union of two classes, written as Turtle writes it: an instance of either is an instance of the union.</summary>
    [Fact]
    public void A_well_formed_asserted_list_is_read()
    {
        (Terms terms, Program program) = Owl();
        Closure closure = new(program, Retraction.DeleteAndRederive);
        closure.Apply(
            [
                terms.Fact(Named("U"), UnionOf, Named("l1")),
                terms.Fact(Named("l1"), First, Named("A")),
                terms.Fact(Named("l1"), Rest, Named("l2")),
                terms.Fact(Named("l2"), First, Named("B")),
                terms.Fact(Named("l2"), Rest, Nil),
                terms.Fact(Named("x"), Type, Named("B")),
            ],
            []);

        Assert.True(closure.Contains(terms.Fact(Named("x"), Type, Named("U"))));
        Assert.Empty(closure.UnreadLists());
    }

    /// <summary>Each way a subject of an asserted <c>rdf:first</c> or <c>rdf:rest</c> fails to be a cell, and each way a chain fails to reach <c>rdf:nil</c>, is reported with its reason.</summary>
    [Fact]
    public void Every_cell_not_read_is_reported_with_its_reason()
    {
        (Terms terms, Program program) = Owl();
        Closure closure = new(program, Retraction.DeleteAndRederive);
        closure.Apply(
            [
                // Two firsts.
                terms.Fact(Named("f"), First, Named("A")),
                terms.Fact(Named("f"), First, Named("B")),
                terms.Fact(Named("f"), Rest, Nil),

                // Two rests.
                terms.Fact(Named("r"), First, Named("A")),
                terms.Fact(Named("r"), Rest, Nil),
                terms.Fact(Named("r"), Rest, Named("f")),

                // A rest and no first, a first and no rest.
                terms.Fact(Named("nf"), Rest, Nil),
                terms.Fact(Named("nr"), First, Named("A")),

                // A cycle of two cells.
                terms.Fact(Named("c1"), First, Named("A")),
                terms.Fact(Named("c1"), Rest, Named("c2")),
                terms.Fact(Named("c2"), First, Named("B")),
                terms.Fact(Named("c2"), Rest, Named("c1")),

                // A chain into a node that is no cell.
                terms.Fact(Named("b1"), First, Named("A")),
                terms.Fact(Named("b1"), Rest, Named("nowhere")),
            ],
            []);

        Dictionary<string, UnreadListReason> unread = closure.UnreadLists().ToDictionary(u => terms.Text(u.Cell), u => u.Reason);
        Assert.Equal(UnreadListReason.SeveralFirsts, unread["<http://example.org/f>"]);
        Assert.Equal(UnreadListReason.SeveralRests, unread["<http://example.org/r>"]);
        Assert.Equal(UnreadListReason.NoFirst, unread["<http://example.org/nf>"]);
        Assert.Equal(UnreadListReason.NoRest, unread["<http://example.org/nr>"]);
        Assert.Equal(UnreadListReason.Cycle, unread["<http://example.org/c1>"]);
        Assert.Equal(UnreadListReason.Cycle, unread["<http://example.org/c2>"]);
        Assert.Equal(UnreadListReason.Broken, unread["<http://example.org/b1>"]);
        Assert.Equal(7, unread.Count);
    }

    /// <summary>An inferred <c>rdf:rest</c> that would give a cell a second rest does not break its list, and one that would give a node its only rest does not make one.</summary>
    [Fact]
    public void An_inferred_rest_neither_makes_nor_breaks_a_list()
    {
        (Terms terms, Program program) = Owl();
        Closure closure = new(program, Retraction.DeleteAndRederive);
        closure.Apply(
            [
                terms.Fact(Named("next"), SubPropertyOf, Rest),

                // A well-formed list of A, with an inferred second rest on its cell.
                terms.Fact(Named("U"), UnionOf, Named("l1")),
                terms.Fact(Named("l1"), First, Named("A")),
                terms.Fact(Named("l1"), Rest, Nil),
                terms.Fact(Named("l1"), Named("next"), Named("m1")),
                terms.Fact(Named("x"), Type, Named("A")),

                // A first asserted and a rest only inferred: no list.
                terms.Fact(Named("V"), UnionOf, Named("m1")),
                terms.Fact(Named("m1"), First, Named("B")),
                terms.Fact(Named("m1"), Named("next"), Nil),
                terms.Fact(Named("y"), Type, Named("B")),
            ],
            []);

        Assert.True(closure.Contains(terms.Fact(Named("l1"), Rest, Named("m1"))), "the inferred rest is a conclusion like any other");
        Assert.True(closure.Contains(terms.Fact(Named("x"), Type, Named("U"))), "an inferred rest breaks no list");
        Assert.False(closure.Contains(terms.Fact(Named("y"), Type, Named("V"))), "an inferred rest makes no list");
        Assert.Equal([UnreadListReason.NoRest], closure.UnreadLists().Select(u => u.Reason));
    }

    /// <summary>
    /// The one-commit case of #399: <c>ex:next rdfs:subPropertyOf rdf:rest</c>
    /// over sixteen nodes that each assert an <c>rdf:first</c> and point
    /// <c>ex:next</c> at every other. Read through inferred facts, the nodes
    /// were a complete graph of cells whose paths had no practical end; read
    /// from the asserted statements, none is a cell, each is reported, and
    /// the closure is the closure from scratch.
    /// </summary>
    [Theory]
    [MemberData(nameof(Strategies))]
    public void One_commit_making_rdf_rest_a_superproperty_reads_no_list_through_it(string strategyName)
    {
        (Terms terms, Program program) = Owl();
        List<Fact> commit = [terms.Fact(Named("next"), SubPropertyOf, Rest), terms.Fact(Named("E"), OneOf, Named("n0"))];
        const int nodes = 16;
        for (int i = 0; i < nodes; i++)
        {
            commit.Add(terms.Fact(Named("n" + i), First, Named("m" + i)));
            commit.Add(terms.Fact(Named("n" + i), Named("next"), Nil));
            for (int j = 0; j < nodes; j++)
            {
                if (i != j)
                {
                    commit.Add(terms.Fact(Named("n" + i), Named("next"), Named("n" + j)));
                }
            }
        }

        Closure closure = new(program, Strategy(strategyName));
        closure.Apply([.. commit], []);

        Assert.True(FromScratch(program, commit).SetEquals(closure.All));
        Assert.False(closure.Contains(terms.Fact(Named("m0"), Type, Named("E"))), "no list is read through an inferred rdf:rest");
        Assert.Equal(nodes, closure.UnreadLists().Count);
        Assert.All(closure.UnreadLists(), u => Assert.Equal(UnreadListReason.NoRest, u.Reason));
    }

    /// <summary>
    /// A change to an asserted list rebuilds the closure: a second asserted
    /// <c>rdf:rest</c> takes the list, and what it alone concluded, away; its
    /// retraction gives them back. At every step the closure is the closure
    /// from scratch, under every strategy.
    /// </summary>
    [Theory]
    [MemberData(nameof(Strategies))]
    public void A_change_to_an_asserted_list_rebuilds_the_closure(string strategyName)
    {
        (Terms terms, Program program) = Owl();
        Closure closure = new(program, Strategy(strategyName));
        HashSet<Fact> data =
        [
            terms.Fact(Named("U"), UnionOf, Named("l1")),
            terms.Fact(Named("l1"), First, Named("A")),
            terms.Fact(Named("l1"), Rest, Named("l2")),
            terms.Fact(Named("l2"), First, Named("B")),
            terms.Fact(Named("l2"), Rest, Nil),
            terms.Fact(Named("x"), Type, Named("B")),
        ];
        Fact union = terms.Fact(Named("x"), Type, Named("U"));
        Fact second = terms.Fact(Named("l2"), Rest, Named("l1"));

        IReadOnlyCollection<Fact> changed = closure.Apply([.. data], []);
        Assert.True(closure.Contains(union));
        Assert.True(FromScratch(program, data).SetEquals(closure.All));

        // A second rest: l2 is no cell, so l1's chain is broken and the union is no list.
        changed = closure.Apply([second], []);
        data.Add(second);
        Assert.False(closure.Contains(union));
        Assert.Contains(union, changed);
        Assert.True(FromScratch(program, data).SetEquals(closure.All));
        Assert.Equal(
            [("<http://example.org/l1>", UnreadListReason.Broken), ("<http://example.org/l2>", UnreadListReason.SeveralRests)],
            closure.UnreadLists().Select(u => (terms.Text(u.Cell), u.Reason)).OrderBy(p => p.Item1, System.StringComparer.Ordinal));

        // Retracted: the list is read again, and the union comes back.
        changed = closure.Apply([], [second]);
        data.Remove(second);
        Assert.True(closure.Contains(union));
        Assert.Contains(union, changed);
        Assert.DoesNotContain(terms.Fact(Named("l1"), First, Named("A")), changed);
        Assert.True(FromScratch(program, data).SetEquals(closure.All));
        Assert.Empty(closure.UnreadLists());
    }

    /// <summary>A data graph reads the lists of the TBox graphs beneath it with its own, reads a TBox cell as the TBox does, and reports only the cells its own statements leave unread or differ on.</summary>
    [Fact]
    public void A_data_graph_reads_the_tbox_lists_and_reports_only_its_own()
    {
        (Terms terms, Program program) = Owl();
        Closure tbox = new(program, Retraction.DeleteAndRederive);
        tbox.Apply(
            [
                terms.Fact(Named("U"), UnionOf, Named("l1")),
                terms.Fact(Named("l1"), First, Named("A")),
                terms.Fact(Named("l1"), Rest, Nil),
                terms.Fact(Named("t"), First, Named("A")),
            ],
            []);
        Closure data = new(program, Retraction.DeleteAndRederive, budget: null, tbox: tbox);
        data.Apply([terms.Fact(Named("x"), Type, Named("A")), terms.Fact(Named("d"), Rest, Nil)], []);

        Assert.True(data.Contains(terms.Fact(Named("x"), Type, Named("U"))));

        // A statement both assert is one statement: the cell keeps its one rest.
        data.Apply([terms.Fact(Named("l1"), Rest, Nil), terms.Fact(Named("V"), UnionOf, Named("l1")), terms.Fact(Named("y"), Type, Named("A"))], []);
        Assert.True(data.Contains(terms.Fact(Named("y"), Type, Named("V"))));

        // A data graph cannot break a TBox list: its second rest on the TBox's cell is reported, and the list is read as the TBox reads it.
        data.Apply([terms.Fact(Named("l1"), Rest, Named("m"))], []);
        Assert.True(data.Contains(terms.Fact(Named("y"), Type, Named("V"))));
        Assert.Contains(("<http://example.org/l1>", UnreadListReason.DiffersFromTBox), data.UnreadLists().Select(u => (terms.Text(u.Cell), u.Reason)));
        Assert.Equal([UnreadListReason.NoRest], tbox.UnreadLists().Select(u => u.Reason));
        Assert.Equal(
            [("<http://example.org/d>", UnreadListReason.NoFirst), ("<http://example.org/l1>", UnreadListReason.DiffersFromTBox)],
            data.UnreadLists().Select(u => (terms.Text(u.Cell), u.Reason)).OrderBy(p => p.Item1, System.StringComparer.Ordinal));
    }
}
