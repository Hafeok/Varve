// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Varve.JsonLd;
using Varve.Rdf;
using Varve.RdfXml;
using Varve.Sparql.Algebra;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The protocol suites with every graph negotiated in RDF/XML and in JSON-LD
/// (ADR 0092, amended 2026-10-12), and every CONSTRUCT and DESCRIBE case of
/// the evaluation suites through the protocol likewise. These are guards,
/// not ratchet lines: each case already passes in its N-Triples or Turtle
/// form, and here it must pass in the other two as well, or be refused with
/// a 406 for a graph that format's own writer refuses — a triple term in
/// JSON-LD, a predicate with no qualified name in RDF/XML — and for nothing
/// else.
/// </summary>
public class GraphFormatConformanceTests
{
    private static readonly string[] Formats = ["application/rdf+xml", "application/ld+json"];

    public static IEnumerable<TheoryDataRow<string, string>> ProtocolCases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (ProtocolEntry entry in ProtocolCatalogue.Entries)
        {
            if (entry.Sequential || !ProtocolRunner.AsksForAGraph(entry))
            {
                continue;
            }

            foreach (string format in Formats)
            {
                yield return new TheoryDataRow<string, string>(entry.TestIri, format) { TestDisplayName = entry.TestIri + " as " + format };
            }
        }
    }

    public static IEnumerable<TheoryDataRow<string, string>> GraphQueryCases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (EvaluationEntry entry in EvaluationCatalogue.Entries)
        {
            if (Exemptions.Covers(entry.TestIri + "@protocol") || EvaluationRunner.ParseQuery(entry) is not (ConstructQuery or DescribeQuery))
            {
                continue;
            }

            foreach (string format in Formats)
            {
                yield return new TheoryDataRow<string, string>(entry.TestIri, format) { TestDisplayName = entry.TestIri + " as " + format };
            }
        }
    }

    [Fact]
    public void Each_list_is_the_size_it_was_when_these_guards_were_written()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        Assert.Equal(11 * Formats.Length, ProtocolCases().Count());
        Assert.Equal(19 * Formats.Length, GraphQueryCases().Count());
    }

    [Theory]
    [MemberData(nameof(ProtocolCases))]
    public async Task A_protocol_case_passes_with_its_graphs_in(string caseIri, string format)
    {
        string? failure = await ProtocolRunner.RunAsync(ProtocolCatalogue.ByIri[caseIri], ProtocolSubject.Memory, TestContext.Current.CancellationToken, format);
        Assert.True(failure is null, caseIri + " as " + format + ": " + failure);
    }

    [Theory]
    [MemberData(nameof(GraphQueryCases))]
    public async Task A_graph_query_through_the_protocol_answers_in(string caseIri, string format)
    {
        EvaluationEntry entry = EvaluationCatalogue.ByIri[caseIri];
        string? failure = await ProtocolEvaluationRunner.RunAsync(entry, TestContext.Current.CancellationToken, format);

        if (failure is not null && failure.StartsWith("406 ", StringComparison.Ordinal))
        {
            // Refused: only for a graph the format's own writer refuses.
            string? refusal = WriterRefuses(format, EvaluationData.Quads(entry.ResultIri!));
            Assert.True(refusal is not null, caseIri + " as " + format + " was refused, but the writer writes the expected graph: " + failure);
            return;
        }

        Assert.True(failure is null, caseIri + " as " + format + ": " + failure);
    }

    // Writes the expected graph with the format's writer: the refusal it
    // gives, or null when it writes it.
    private static string? WriterRefuses(string format, IReadOnlyList<DataQuad> graph)
    {
        ArrayBufferWriter output = new();
        TermArena arena = new();

        try
        {
            if (format == "application/rdf+xml")
            {
                using RdfXmlWriter writer = new(output, default);

                foreach (DataQuad quad in graph)
                {
                    QuadView view = View(arena, quad);
                    writer.Write(in view);
                }
            }
            else
            {
                using JsonLdWriter writer = new(output, default);

                foreach (DataQuad quad in graph)
                {
                    QuadView view = View(arena, quad);
                    writer.Write(in view);
                }
            }

            return null;
        }
        catch (InvalidOperationException refused)
        {
            return refused.Message;
        }
    }

    private static QuadView View(TermArena arena, DataQuad quad)
    {
        arena.Reset();
        int s = Add(arena, quad.Subject);
        int p = Add(arena, quad.Predicate);
        int o = Add(arena, quad.Object);
        return arena.Quad(default, s, p, o, -1);
    }

    private static int Add(TermArena arena, RdfTerm term) => term.Kind switch
    {
        RdfTermKind.Iri => arena.AddIri(arena.AppendScratch(term.Lexical)),
        RdfTermKind.BlankNode => arena.AddBlankNode(arena.AppendScratch(term.Lexical)),
        RdfTermKind.Literal when !term.Language.IsEmpty => arena.AddLiteral(arena.AppendScratch(term.Lexical), TermSpan.None, arena.AppendScratch(term.Language), term.Direction),
        RdfTermKind.Literal => arena.AddLiteral(arena.AppendScratch(term.Lexical), term.DatatypeIri.IsEmpty ? TermSpan.None : arena.AppendScratch(term.DatatypeIri), TermSpan.None, TextDirection.None),
        _ => AddTriple(arena, term),
    };

    private static int AddTriple(TermArena arena, RdfTerm term)
    {
        int s = Add(arena, term.Subject!);
        int p = Add(arena, term.Predicate!);
        int o = Add(arena, term.Object!);
        return arena.AddTripleTerm(s, p, o);
    }
}
