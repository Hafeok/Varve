// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using Varve.Rdf;
using Xunit;

namespace Varve.Sparql.Results.Tests;

public class ReaderTests
{
    private const string Xsd = "http://www.w3.org/2001/XMLSchema#";

    // ------------------------------------------------------------------ XML

    [Fact]
    public void Xml_reads_every_term_kind()
    {
        const string document = """
            <?xml version="1.0"?>
            <!-- a comment before the root -->
            <sparql xmlns="http://www.w3.org/2005/sparql-results#" xmlns:its="http://www.w3.org/2005/11/its">
              <head><variable name="a"/><variable name="b"/><link href="x"/></head>
              <results>
                <result>
                  <binding name="a"><uri>http://e/&amp;x</uri></binding>
                  <binding name="b"><literal xml:lang="ar" its:dir="rtl">&#x642;<![CDATA[<raw>]]></literal></binding>
                </result>
                <result>
                  <binding name="b"><literal datatype="http://www.w3.org/2001/XMLSchema#integer">1</literal></binding>
                </result>
                <result>
                  <binding name="a"><bnode>b0</bnode></binding>
                  <binding name="b"><triple><subject><uri>http://e/s</uri></subject><predicate><uri>http://e/p</uri></predicate><object><literal/></object></triple></binding>
                </result>
              </results>
            </sparql>
            """;

        Assert.Equal(
            "vars a b\n"
            + "<http://e/&x> | \"ق<raw>\"@ar--rtl\n"
            + "- | \"1\"^^<" + Xsd + "integer>\n"
            + "_:b0 | <<( <http://e/s> <http://e/p> \"\" )>>\n",
            ResultDocument.Render(document, SparqlResultsFormat.Xml));
    }

    [Fact]
    public void Xml_reads_a_prefixed_results_namespace_and_a_boolean()
    {
        const string document = "<r:sparql xmlns:r='http://www.w3.org/2005/sparql-results#'><r:head/><r:boolean> true </r:boolean></r:sparql>";
        Assert.Equal("boolean true\n", ResultDocument.Render(document, SparqlResultsFormat.Xml));
    }

    [Fact]
    public void Xml_refuses_a_document_type_declaration_where_it_starts()
    {
        const string document = "<?xml version=\"1.0\"?>\n<!DOCTYPE sparql [ <!ENTITY x \"y\"> ]>\n<sparql/>";
        Assert.Equal(
            "error DocumentTypeDeclaration at line 2, column 1 (byte 22)",
            ResultDocument.Render(document, SparqlResultsFormat.Xml));
    }

    [Fact]
    public void Xml_reports_a_mismatched_end_tag_at_the_end_tag()
    {
        const string document = "<sparql xmlns='http://www.w3.org/2005/sparql-results#'>\n<head>\n</heap></sparql>";
        Assert.Equal(
            "error Malformed at line 3, column 1 (byte 63)",
            ResultDocument.Render(document, SparqlResultsFormat.Xml));
    }

    [Fact]
    public void Xml_refuses_a_binding_to_an_undeclared_variable()
    {
        const string document = "<sparql xmlns='http://www.w3.org/2005/sparql-results#'><head><variable name='a'/></head><results><result><binding name='z'><uri>x</uri></binding></result></results></sparql>";
        Assert.StartsWith("vars a\nerror UnknownVariable at line 1, column 106 (byte 105)", ResultDocument.Render(document, SparqlResultsFormat.Xml), StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_refuses_an_unknown_entity()
    {
        const string document = "<sparql xmlns='http://www.w3.org/2005/sparql-results#'><head><variable name='a'/></head><results><result><binding name='a'><uri>&nbsp;</uri></binding></result></results></sparql>";
        Assert.EndsWith("error Malformed at line 1, column 129 (byte 128)", ResultDocument.Render(document, SparqlResultsFormat.Xml), StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- JSON

    [Fact]
    public void Json_reads_the_results_before_the_head()
    {
        const string document = """
            { "results": { "bindings": [ { "x": { "value": "v", "type": "literal", "xml:lang": "en", "its:dir": "ltr" } } ] },
              "head": { "vars": [ "x" ] } }
            """;
        Assert.Equal("vars x\n\"v\"@en--ltr\n", ResultDocument.Render(document, SparqlResultsFormat.Json));
    }

    [Fact]
    public void Json_reads_a_triple_and_a_legacy_typed_literal()
    {
        const string document = """
            { "head": { "vars": [ "t", "n" ] }, "results": { "bindings": [ {
              "t": { "type": "triple", "value": { "subject": { "type": "bnode", "value": "b" }, "predicate": { "type": "uri", "value": "http://e/p" }, "object": { "type": "literal", "value": "o\u00e9" } } },
              "n": { "type": "typed-literal", "datatype": "http://www.w3.org/2001/XMLSchema#decimal", "value": "1.5" } } ] } }
            """;
        Assert.Equal("vars t n\n<<( _:b <http://e/p> \"oé\" )>> | \"1.5\"^^<" + Xsd + "decimal>\n", ResultDocument.Render(document, SparqlResultsFormat.Json));
    }

    [Fact]
    public void Json_reports_malformed_json_where_it_is()
    {
        const string document = "{ \"head\": { \"vars\": [ \"x\" ] },\n  \"results\": { \"bindings\": [ { \"x\": { \"type\": \"uri\" \"value\": \"a\" } } ] } }";
        Assert.Equal("vars x\nerror Malformed at line 2, column 53 (byte 83)", ResultDocument.Render(document, SparqlResultsFormat.Json));
    }

    [Fact]
    public void Json_refuses_an_unknown_term_type()
    {
        const string document = "{ \"head\": { \"vars\": [ \"x\" ] }, \"results\": { \"bindings\": [ { \"x\": { \"type\": \"url\", \"value\": \"a\" } } ] } }";
        Assert.Equal("vars x\nerror InvalidTerm at line 1, column 76 (byte 75)", ResultDocument.Render(document, SparqlResultsFormat.Json));
    }

    // ------------------------------------------------------------------ TSV

    [Fact]
    public void Tsv_reads_turtle_terms()
    {
        const string document = "?a\t?b\t?c\n<http://e/x>\t\"q\\t\\\"\\u00e9\"@en\t12\n_:b\t-1.5e3\t\"x\"^^<http://e/t>\n\"r\"@ar--rtl\ttrue\t<<( <http://e/s> <http://e/p> 1.0 )>>\r\n";
        Assert.Equal(
            "vars a b c\n"
            + "<http://e/x> | \"q\t\"é\"@en | \"12\"^^<" + Xsd + "integer>\n"
            + "_:b | \"-1.5e3\"^^<" + Xsd + "double> | \"x\"^^<http://e/t>\n"
            + "\"r\"@ar--rtl | \"true\"^^<" + Xsd + "boolean> | <<( <http://e/s> <http://e/p> \"1.0\"^^<" + Xsd + "decimal> )>>\n",
            ResultDocument.Render(document, SparqlResultsFormat.Tsv));
    }

    [Fact]
    public void Tsv_reads_unbound_as_an_empty_field()
    {
        Assert.Equal("vars a b\n- | <http://e/x>\n", ResultDocument.Render("?a\t?b\n\t<http://e/x>\n", SparqlResultsFormat.Tsv));
    }

    [Fact]
    public void Tsv_reports_a_bad_term_at_its_column()
    {
        Assert.Equal("vars a b\nerror InvalidTerm at line 2, column 8 (byte 13)", ResultDocument.Render("?a\t?b\n<x>\t\"ab\\z\"\n", SparqlResultsFormat.Tsv));
    }

    [Fact]
    public void Tsv_refuses_a_row_of_the_wrong_width()
    {
        Assert.Equal("vars a b\nerror FieldCount at line 3, column 1 (byte 10)", ResultDocument.Render("?a\t?b\n<x>\n", SparqlResultsFormat.Tsv));
    }

    // ------------------------------------------------------------------ CSV

    [Fact]
    public void Csv_reads_quoted_fields_blank_nodes_and_unbound()
    {
        const string document = "a,b,c\r\nhttp://e/x,\"say \"\"hi\"\", then\r\nleave\",_:b1\r\n,\"\",x\r\n";
        Assert.Equal(
            "vars a b c\n\"http://e/x\" | \"say \"hi\", then\r\nleave\" | _:b1\n- | \"\" | \"x\"\n",
            ResultDocument.Render(document, SparqlResultsFormat.Csv));
    }

    [Fact]
    public void Csv_reports_an_unterminated_quote_where_it_opened()
    {
        Assert.Equal("vars a\nerror UnexpectedEnd at line 2, column 1 (byte 2)", ResultDocument.Render("a\n\"abc", SparqlResultsFormat.Csv));
    }

    // ------------------------------------------------------------ the reader

    [Fact]
    public void Current_is_refused_when_no_solution_is_current()
    {
        SparqlResultsReader reader = new(Encoding.UTF8.GetBytes("?a\n"), SparqlResultsFormat.Tsv);
        Assert.False(reader.Read());
        Assert.Throws<InvalidOperationException>(() => { _ = reader.Current.Count; });
    }

    [Theory]
    [InlineData(SparqlResultsFormat.Xml)]
    [InlineData(SparqlResultsFormat.Json)]
    [InlineData(SparqlResultsFormat.Tsv)]
    [InlineData(SparqlResultsFormat.Csv)]
    public void Reading_allocates_nothing_per_solution_once_warm(SparqlResultsFormat format)
    {
        byte[] small = Document(format, 1_000);
        byte[] large = Document(format, 10_000);

        // The first large read grows the arena and the builders to their size;
        // the meter's rounds repeat until one reproduces the last (issue #32).
        (long smallCost, long largeCost) = AllocationMeter.MeasurePair(
            () =>
            {
                Consume(small, format);
                return null;
            },
            () =>
            {
                Consume(large, format);
                return null;
            });

        // A reader is a handful of objects, and those are the same at any size:
        // per solution the difference must be zero.
        double perSolution = (largeCost - smallCost) / 9_000.0;
        Assert.True(
            perSolution < 0.5,
            string.Create(CultureInfo.InvariantCulture, $"{format}: {perSolution:F2} bytes per solution ({smallCost} for 1,000, {largeCost} for 10,000)"));
    }

    private static int Consume(byte[] document, SparqlResultsFormat format)
    {
        SparqlResultsReader reader = new(new ReadOnlySequence<byte>(document), format);
        int bound = 0;
        while (reader.Read())
        {
            SolutionView solution = reader.Current;
            for (int i = 0; i < solution.Count; i++)
            {
                if (solution.TryGet(i, out RdfTermView term))
                {
                    bound += term.Lexical.Length;
                }
            }
        }

        Assert.False(reader.Error.IsError, reader.Error.ToString());
        return bound;
    }

    private static byte[] Document(SparqlResultsFormat format, int solutions)
    {
        StringBuilder text = new();
        switch (format)
        {
            case SparqlResultsFormat.Xml:
                text.Append("<sparql xmlns='http://www.w3.org/2005/sparql-results#'><head><variable name='s'/><variable name='o'/></head><results>");
                for (int i = 0; i < solutions; i++)
                {
                    text.Append(CultureInfo.InvariantCulture, $"<result><binding name='s'><uri>http://e/s{i % 97}</uri></binding><binding name='o'><literal datatype='{Xsd}integer'>{i % 89}</literal></binding></result>");
                }

                text.Append("</results></sparql>");
                break;
            case SparqlResultsFormat.Json:
                text.Append("{\"head\":{\"vars\":[\"s\",\"o\"]},\"results\":{\"bindings\":[");
                for (int i = 0; i < solutions; i++)
                {
                    text.Append(i == 0 ? "" : ",").Append(CultureInfo.InvariantCulture, $"{{\"s\":{{\"type\":\"uri\",\"value\":\"http://e/s{i % 97}\"}},\"o\":{{\"type\":\"literal\",\"datatype\":\"{Xsd}integer\",\"value\":\"{i % 89}\"}}}}");
                }

                text.Append("]}}");
                break;
            case SparqlResultsFormat.Tsv:
                text.Append("?s\t?o\n");
                for (int i = 0; i < solutions; i++)
                {
                    text.Append(CultureInfo.InvariantCulture, $"<http://e/s{i % 97}>\t{i % 89}\n");
                }

                break;
            default:
                text.Append("s,o\r\n");
                for (int i = 0; i < solutions; i++)
                {
                    text.Append(CultureInfo.InvariantCulture, $"http://e/s{i % 97},{i % 89}\r\n");
                }

                break;
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }
}
