// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Results;
using Varve.Xsd;

namespace Varve.Conformance.Tests;

/// <summary>
/// A SELECT or ASK answer as the harness compares it: variables and rows of
/// terms, or a boolean; and how to compare two (<c>sparql-evaluation.md</c>
/// §12.1).
/// </summary>
internal sealed class ResultTable
{
    private const string Rs = "http://www.w3.org/2001/sw/DataAccess/tests/result-set#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    internal List<string> Variables { get; } = [];

    internal List<RdfTerm?[]> Rows { get; } = [];

    internal bool? Boolean { get; set; }

    /// <summary>A CSV result carries lexical forms only, so it is compared by them.</summary>
    internal bool LexicalOnly { get; set; }

    /// <summary>Whether the rows' order means something: an ORDER BY in the query, or rs:index in the file.</summary>
    internal bool Ordered { get; set; }

    /// <summary>Reads an expected result file, by its extension; null when the file is a graph instead.</summary>
    internal static ResultTable? Read(string path, IReadOnlyList<DataQuad>? graph)
    {
        string extension = Path.GetExtension(path);
        SparqlResultsFormat? format = extension switch
        {
            ".srx" => SparqlResultsFormat.Xml,
            ".srj" => SparqlResultsFormat.Json,
            ".tsv" => SparqlResultsFormat.Tsv,
            ".csv" => SparqlResultsFormat.Csv,
            _ => null,
        };

        if (format is { } f)
        {
            return FromDocument(File.ReadAllBytes(path), f);
        }

        return graph is null ? null : FromResultSetGraph(graph);
    }

    private static ResultTable FromDocument(byte[] bytes, SparqlResultsFormat format)
    {
        SparqlResultsReader reader = new(new ReadOnlySequence<byte>(bytes), format);
        ResultTable table = new() { LexicalOnly = format == SparqlResultsFormat.Csv };
        if (!reader.ReadHead())
        {
            throw new InvalidOperationException("The expected result does not read: " + reader.Error);
        }

        if (reader.IsBoolean)
        {
            table.Boolean = reader.Boolean;
            return table;
        }

        table.Variables.AddRange(reader.Variables);
        while (reader.Read())
        {
            SolutionView solution = reader.Current;
            RdfTerm?[] row = new RdfTerm?[solution.Count];
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = solution.TryGet(i, out RdfTermView term) ? term.Materialise() : null;
            }

            table.Rows.Add(row);
        }

        if (reader.Error.IsError)
        {
            throw new InvalidOperationException("The expected result does not read: " + reader.Error);
        }

        return table;
    }

    /// <summary>A result set written in RDF with the rs: vocabulary, as the SPARQL 1.0 suites do; null if the graph is not one.</summary>
    private static ResultTable? FromResultSetGraph(IReadOnlyList<DataQuad> quads)
    {
        Dictionary<RdfTerm, List<(RdfTerm P, RdfTerm O)>> bySubject = new(RdfTerm.Comparer);
        foreach (DataQuad quad in quads)
        {
            if (!bySubject.TryGetValue(quad.Subject, out List<(RdfTerm, RdfTerm)>? list))
            {
                list = [];
                bySubject.Add(quad.Subject, list);
            }

            list.Add((quad.Predicate, quad.Object));
        }

        RdfTerm? set = bySubject.FirstOrDefault(pair => pair.Value.Any(po => Is(po.P, Rdf + "type") && Is(po.O, Rs + "ResultSet"))).Key;
        if (set is null)
        {
            return null;
        }

        ResultTable table = new();
        List<(RdfTerm P, RdfTerm O)> properties = bySubject[set];
        foreach ((RdfTerm p, RdfTerm o) in properties)
        {
            if (Is(p, Rs + "boolean"))
            {
                table.Boolean = Encoding.UTF8.GetString(o.Lexical) == "true";
            }
            else if (Is(p, Rs + "resultVariable"))
            {
                table.Variables.Add(Encoding.UTF8.GetString(o.Lexical));
            }
        }

        List<(long Index, RdfTerm?[] Row)> rows = [];
        foreach ((RdfTerm p, RdfTerm solution) in properties)
        {
            if (!Is(p, Rs + "solution"))
            {
                continue;
            }

            RdfTerm?[] row = new RdfTerm?[table.Variables.Count];
            long index = -1;
            foreach ((RdfTerm sp, RdfTerm so) in bySubject.GetValueOrDefault(solution) ?? [])
            {
                if (Is(sp, Rs + "index"))
                {
                    index = long.Parse(Encoding.UTF8.GetString(so.Lexical), CultureInfo.InvariantCulture);
                }
                else if (Is(sp, Rs + "binding"))
                {
                    List<(RdfTerm P, RdfTerm O)> binding = bySubject[so];
                    string variable = Encoding.UTF8.GetString(binding.First(b => Is(b.P, Rs + "variable")).O.Lexical);
                    RdfTerm value = binding.First(b => Is(b.P, Rs + "value")).O;
                    int column = table.Variables.IndexOf(variable);
                    if (column < 0)
                    {
                        table.Variables.Add(variable);
                        Array.Resize(ref row, table.Variables.Count);
                        column = table.Variables.Count - 1;
                    }

                    row[column] = value;
                }
            }

            rows.Add((index, row));
        }

        if (rows.Any(r => r.Index >= 0))
        {
            table.Ordered = true;
            rows.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        foreach ((_, RdfTerm?[] row) in rows)
        {
            RdfTerm?[] full = row;
            Array.Resize(ref full, table.Variables.Count);
            table.Rows.Add(full);
        }

        return table;
    }

    private static bool Is(RdfTerm term, string iri) =>
        term.Kind == RdfTermKind.Iri && term.Lexical.SequenceEqual(Encoding.UTF8.GetBytes(iri));

    // ------------------------------------------------------------ comparison

    /// <summary>
    /// Compares an actual answer with this expected one: null when they agree,
    /// otherwise why not. Rows are matched as multisets under one blank node
    /// bijection; in order when <paramref name="ordered"/>, as a sequence of
    /// groups of rows that agree on <paramref name="orderKeys"/> — each row its
    /// own group when no key is projected; as sets under
    /// <paramref name="lax"/>.
    /// </summary>
    internal string? Compare(ResultTable actual, bool ordered, IReadOnlyList<string> orderKeys, bool lax)
    {
        if (Boolean is { } expectedBoolean)
        {
            return actual.Boolean == expectedBoolean ? null : $"ASK answered {actual.Boolean?.ToString() ?? "a table"}, expected {expectedBoolean}";
        }

        if (actual.Boolean is not null)
        {
            return "answered a boolean, expected a table";
        }

        // Align the columns by name: a result file may order them differently.
        List<RdfTerm?[]> actualRows = [];
        foreach (RdfTerm?[] row in actual.Rows)
        {
            RdfTerm?[] aligned = new RdfTerm?[Variables.Count];
            for (int i = 0; i < Variables.Count; i++)
            {
                int column = actual.Variables.IndexOf(Variables[i]);
                aligned[i] = column >= 0 ? row[column] : null;
            }

            for (int j = 0; j < actual.Variables.Count; j++)
            {
                if (!Variables.Contains(actual.Variables[j]) && row[j] is not null)
                {
                    return "binds ?" + actual.Variables[j] + ", which the expected result does not have";
                }
            }

            actualRows.Add(aligned);
        }

        List<RdfTerm?[]> expectedRows = Rows;
        if (lax)
        {
            expectedRows = Distinct(expectedRows);
            actualRows = Distinct(actualRows);
        }

        if (actualRows.Count != expectedRows.Count)
        {
            return $"{actualRows.Count} row(s), expected {expectedRows.Count}\n" + Show("actual", actualRows) + Show("expected", expectedRows);
        }

        Dictionary<string, string> bijection = new(StringComparer.Ordinal);
        Dictionary<string, string> inverse = new(StringComparer.Ordinal);
        bool matched;
        if (ordered || Ordered)
        {
            matched = MatchOrdered(actualRows, expectedRows, orderKeys, bijection, inverse);
        }
        else
        {
            matched = MatchMultiset(actualRows, expectedRows, bijection, inverse);
        }

        return matched ? null : "the rows differ\n" + Show("actual", actualRows) + Show("expected", expectedRows);
    }

    private bool MatchOrdered(List<RdfTerm?[]> actual, List<RdfTerm?[]> expected, IReadOnlyList<string> keys, Dictionary<string, string> bijection, Dictionary<string, string> inverse)
    {
        int[] columns = [.. keys.Select(k => Variables.IndexOf(k)).Where(c => c >= 0)];
        int i = 0;
        while (i < expected.Count)
        {
            int j = i + 1;
            while (j < expected.Count && columns.Length > 0 && columns.All(c => Key(expected[j][c]) == Key(expected[i][c])))
            {
                j++;
            }

            if (!MatchMultiset(actual.GetRange(i, j - i), expected.GetRange(i, j - i), bijection, inverse))
            {
                return false;
            }

            i = j;
        }

        return true;
    }

    private static string Key(RdfTerm? term) => term is null ? "" : EvaluationData.Text(term);

    /// <summary>A backtracking search for a row matching under one blank node bijection.</summary>
    private bool MatchMultiset(List<RdfTerm?[]> actual, List<RdfTerm?[]> expected, Dictionary<string, string> bijection, Dictionary<string, string> inverse)
    {
        bool[] used = new bool[expected.Count];
        int attempts = 0;
        return Assign(0);

        bool Assign(int index)
        {
            if (index == actual.Count)
            {
                return true;
            }

            if (++attempts > 200_000)
            {
                return false;
            }

            for (int e = 0; e < expected.Count; e++)
            {
                if (used[e])
                {
                    continue;
                }

                List<string> added = [];
                if (RowMatches(actual[index], expected[e], bijection, inverse, added))
                {
                    used[e] = true;
                    if (Assign(index + 1))
                    {
                        return true;
                    }

                    used[e] = false;
                }

                foreach (string label in added)
                {
                    inverse.Remove(bijection[label]);
                    bijection.Remove(label);
                }
            }

            return false;
        }
    }

    private bool RowMatches(RdfTerm?[] actual, RdfTerm?[] expected, Dictionary<string, string> bijection, Dictionary<string, string> inverse, List<string> added)
    {
        for (int i = 0; i < actual.Length; i++)
        {
            if (!TermMatches(actual[i], expected[i], bijection, inverse, added))
            {
                return false;
            }
        }

        return true;
    }

    private bool TermMatches(RdfTerm? actual, RdfTerm? expected, Dictionary<string, string> bijection, Dictionary<string, string> inverse, List<string> added)
    {
        if (actual is null || expected is null)
        {
            // CSV writes an unbound variable and an empty string alike.
            return actual is null && expected is null
                || (LexicalOnly && (actual ?? expected)!.Kind == RdfTermKind.Literal && (actual ?? expected)!.Lexical.IsEmpty);
        }

        if (expected.Kind == RdfTermKind.BlankNode || (LexicalOnly && actual.Kind == RdfTermKind.BlankNode))
        {
            if (actual.Kind != RdfTermKind.BlankNode || expected.Kind != RdfTermKind.BlankNode)
            {
                return false;
            }

            string a = Encoding.UTF8.GetString(actual.Lexical);
            string e = Encoding.UTF8.GetString(expected.Lexical);
            if (bijection.TryGetValue(a, out string? mapped))
            {
                return mapped == e;
            }

            if (inverse.ContainsKey(e))
            {
                return false;
            }

            bijection[a] = e;
            inverse[e] = a;
            added.Add(a);
            return true;
        }

        if (expected.Kind == RdfTermKind.TripleTerm)
        {
            return actual.Kind == RdfTermKind.TripleTerm
                && TermMatches(actual.Subject, expected.Subject, bijection, inverse, added)
                && TermMatches(actual.Predicate, expected.Predicate, bijection, inverse, added)
                && TermMatches(actual.Object, expected.Object, bijection, inverse, added);
        }

        if (LexicalOnly)
        {
            return actual.Kind != RdfTermKind.TripleTerm && actual.Lexical.SequenceEqual(expected.Lexical);
        }

        return actual.Equals(expected) || SameValue(actual, expected);
    }

    /// <summary>
    /// Two literals of the same numeric or boolean datatype with equal values:
    /// the suites' expected results write those in several lexical forms for
    /// one value (<c>sparql11/cast</c> has <c>"1.0"</c>, <c>"1"</c> and
    /// <c>"1.0E0"</c> for float), so no implementation can match them as terms.
    /// </summary>
    internal static bool SameValue(RdfTerm actual, RdfTerm expected)
    {
        if (actual.Kind != RdfTermKind.Literal || expected.Kind != RdfTermKind.Literal
            || actual.Datatype is null || expected.Datatype is null
            || !actual.DatatypeIri.SequenceEqual(expected.DatatypeIri))
        {
            return false;
        }

        XsdDatatype datatype = XsdDatatypes.FromIri(expected.DatatypeIri);
        if (XsdDatatypes.IsNumeric(datatype))
        {
            return XsdNumeric.TryParse(actual.Lexical, datatype, out XsdNumeric a)
                && XsdNumeric.TryParse(expected.Lexical, datatype, out XsdNumeric b)
                && (a.Equals(b) || (a.Kind is XsdNumericKind.Float or XsdNumericKind.Double && double.IsNaN(a.AsDouble.Value) && double.IsNaN(b.AsDouble.Value)));
        }

        return datatype == XsdDatatype.Boolean
            && XsdBoolean.TryParse(actual.Lexical, out XsdBoolean x)
            && XsdBoolean.TryParse(expected.Lexical, out XsdBoolean y)
            && x.Value == y.Value;
    }

    private static List<RdfTerm?[]> Distinct(List<RdfTerm?[]> rows)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<RdfTerm?[]> distinct = [];
        foreach (RdfTerm?[] row in rows)
        {
            if (seen.Add(string.Join("|", row.Select(Key))))
            {
                distinct.Add(row);
            }
        }

        return distinct;
    }

    private string Show(string label, List<RdfTerm?[]> rows)
    {
        StringBuilder text = new();
        text.Append("  ").Append(label).Append(": ").AppendJoin(' ', Variables.Select(v => "?" + v)).Append('\n');
        foreach (RdfTerm?[] row in rows.Take(40))
        {
            text.Append("    ").AppendJoin(" | ", row.Select(t => t is null ? "-" : EvaluationData.Text(t))).Append('\n');
        }

        if (rows.Count > 40)
        {
            text.Append("    … ").Append(rows.Count - 40).Append(" more\n");
        }

        return text.ToString();
    }
}
