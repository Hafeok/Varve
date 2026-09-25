// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Results;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The writer side of the result-format suites (<c>sparql-results.md</c> §6):
/// each <c>csv-tsv-res</c> and <c>json-res</c> case evaluated over the store,
/// its results written in the expected file's format, and the output compared
/// with the expected file — line by line up to blank node labels (and, for
/// CSV, line endings) where the format is canonical, by re-reading where it is
/// not. One ratchet line per case: <c>&lt;test IRI&gt;@writer</c>.
/// </summary>
public class ResultWriterConformanceTests
{
    internal static readonly string[] Suites = ["sparql11/csv-tsv-res", "sparql11/json-res"];

    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (EvaluationEntry entry in EvaluationCatalogue.Entries)
        {
            if (Suites.Contains(entry.Suite))
            {
                string id = entry.TestIri + "@writer";
                yield return new TheoryDataRow<string>(id) { TestDisplayName = id };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Case(string testIri)
    {
        EvaluationEntry entry = EvaluationCatalogue.ByIri[testIri[..testIri.LastIndexOf('@')]];
        string expectedPath = EvaluationSuite.PathOf(entry.ResultIri!);
        SparqlResultsFormat format = FormatOf(expectedPath);

        byte[] written = await EvaluationRunner.EvaluateAsync(
            entry,
            EvaluationSubjects.Store,
            ValueAccess.InlineAccessor,
            (_, results) => Write(results, format),
            TestContext.Current.CancellationToken);

        byte[] expected = File.ReadAllBytes(expectedPath);
        // Lines first, where the format is canonical; where they differ — TSV
        // may abbreviate a number or not, and tsv03's expected file writes the
        // data's "1.0E6"^^xsd:double as 1.0e6 — by reading both documents.
        string? lines = format is SparqlResultsFormat.Csv or SparqlResultsFormat.Tsv
            ? CompareLines(written, expected, format)
            : "not a line format";
        string? failure = lines is null ? null : CompareByReading(written, expectedPath);
        string verdict = written.AsSpan().SequenceEqual(expected) ? "byte-equal"
            : lines is null ? Equivalence(format)
            : failure is null ? "equal on re-reading (" + lines + ")"
            : "differs";

        TestContext.Current.TestOutputHelper?.WriteLine(verdict);
        Assert.True(failure is null, entry.Suite + " " + entry.Name + ": " + failure
            + "\n--- written:\n" + Encoding.UTF8.GetString(written) + "\n--- expected:\n" + Encoding.UTF8.GetString(expected));
    }

    private static string Equivalence(SparqlResultsFormat format) =>
        format == SparqlResultsFormat.Csv ? "line-equal up to blank node labels and CRLF" : "line-equal up to blank node labels";

    private static SparqlResultsFormat FormatOf(string path) => Path.GetExtension(path) switch
    {
        ".csv" => SparqlResultsFormat.Csv,
        ".tsv" => SparqlResultsFormat.Tsv,
        ".srj" => SparqlResultsFormat.Json,
        ".srx" => SparqlResultsFormat.Xml,
        _ => throw new InvalidOperationException("Not a result format: " + path),
    };

    private static byte[] Write(QueryResults results, SparqlResultsFormat format)
    {
        ArrayBufferWriter<byte> output = new();

        using (SparqlResultsWriter writer = new(output, format))
        {
            switch (results)
            {
                case SolutionResults solutions:
                    writer.WriteHead([.. solutions.Variables.Select(v => v.Name)]);

                    while (solutions.MoveNext())
                    {
                        writer.StartSolution();

                        for (int i = 0; i < solutions.Variables.Count; i++)
                        {
                            if (solutions.TryGetTerm(i, out RdfTerm? term))
                            {
                                writer.WriteBinding(i, term);
                            }
                        }

                        writer.EndSolution();
                    }

                    break;

                case BooleanResult boolean:
                    writer.WriteBoolean(boolean.Value);
                    break;

                default:
                    throw new InvalidOperationException("Not a SELECT or an ASK.");
            }

            writer.WriteEnd();
        }

        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Line by line, after renaming blank node labels in order of first
    /// appearance on each side — the queries order their solutions, so the
    /// order is the suite's — and, for CSV, reading CRLF as LF: the expected
    /// files end lines with LF, where RFC 4180 has CRLF (§5.3).
    /// </summary>
    private static string? CompareLines(byte[] written, byte[] expected, SparqlResultsFormat format)
    {
        string[] actual = Lines(written, format);
        string[] wanted = Lines(expected, format);

        if (actual.Length != wanted.Length)
        {
            return actual.Length + " line(s), expected " + wanted.Length;
        }

        for (int i = 0; i < actual.Length; i++)
        {
            if (actual[i] != wanted[i])
            {
                return "line " + (i + 1) + " is '" + actual[i] + "', expected '" + wanted[i] + "'";
            }
        }

        return null;
    }

    private static string[] Lines(byte[] document, SparqlResultsFormat format)
    {
        string text = Encoding.UTF8.GetString(document);

        if (format == SparqlResultsFormat.Csv)
        {
            text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        List<string> lines = [.. text.Split('\n')];

        // A final line ending is optional in both formats.
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        Dictionary<string, string> labels = new(StringComparer.Ordinal);
        char separator = format == SparqlResultsFormat.Csv ? ',' : '\t';

        return [.. lines.Select(line => string.Join(separator, line.Split(separator).Select(field => Relabel(field, labels))))];
    }

    private static string Relabel(string field, Dictionary<string, string> labels)
    {
        if (!field.StartsWith("_:", StringComparison.Ordinal))
        {
            return field;
        }

        if (!labels.TryGetValue(field, out string? label))
        {
            label = "_:n" + labels.Count;
            labels[field] = label;
        }

        return label;
    }

    /// <summary>
    /// JSON's whitespace and member order are not canonical: both documents
    /// are read, and compared as the evaluation comparator compares results.
    /// </summary>
    private static string? CompareByReading(byte[] written, string expectedPath)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "varve-writer-" + Guid.NewGuid().ToString("N") + Path.GetExtension(expectedPath));

        try
        {
            File.WriteAllBytes(temporary, written);
            ResultTable actual = ResultTable.Read(temporary, null)
                ?? throw new InvalidOperationException("The written document did not read as a result.");
            ResultTable expected = ResultTable.Read(expectedPath, null)
                ?? throw new InvalidOperationException("The expected document did not read as a result.");
            return expected.Compare(actual, ordered: true, [], lax: false);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
