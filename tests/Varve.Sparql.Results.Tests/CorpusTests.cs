// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Xunit;

namespace Varve.Sparql.Results.Tests;

/// <summary>
/// Every result document in the pinned W3C SPARQL suites: it reads without
/// error, it reads the same split anywhere (the chunk-boundary oracle,
/// <c>docs/testing.md</c> §2), and where one test's result exists in two
/// formats, both read to the same solutions.
/// </summary>
public class CorpusTests
{
    /// <summary>Above this size a document is split at a stride rather than at every offset.</summary>
    private const int EveryOffsetUpTo = 4096;

    /// <summary>
    /// Documents the suites deliberately ship malformed, or that are not
    /// SPARQL results at all despite the extension. Each is named with why.
    /// </summary>
    private static readonly Dictionary<string, string> NotResults = new(StringComparer.Ordinal)
    {
    };

    public static IEnumerable<TheoryDataRow<string>> Documents()
    {
        foreach (string file in ResultDocument.Corpus())
        {
            yield return new TheoryDataRow<string>(file) { TestDisplayName = file };
        }
    }

    [Fact]
    public void The_corpus_is_there()
    {
        Assert.True(Directory.Exists(ResultDocument.SparqlSuites), "The W3C submodule is not checked out: git submodule update --init.");
        int count = 0;
        foreach (string unused in ResultDocument.Corpus())
        {
            count++;
        }

        // Pinned: a submodule bump that adds or removes a result file changes this,
        // and the change is then looked at rather than absorbed.
        Assert.Equal(ExpectedCorpusSize, count);
    }

    /// <summary>The number of .srx, .srj, .tsv and .csv files under sparql/ at the pinned commit.</summary>
    private const int ExpectedCorpusSize = 508;

    [Theory]
    [MemberData(nameof(Documents))]
    public void Every_suite_result_document_reads(string file)
    {
        string path = Path.Combine(ResultDocument.SparqlSuites, file);
        string rendered = ResultDocument.Render(File.ReadAllBytes(path), ResultDocument.FormatOf(path)!.Value);
        if (NotResults.ContainsKey(file))
        {
            Assert.Contains("error ", rendered, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("error ", rendered, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void The_answer_does_not_depend_on_where_the_input_was_split(string file)
    {
        string path = Path.Combine(ResultDocument.SparqlSuites, file);
        byte[] bytes = File.ReadAllBytes(path);
        SparqlResultsFormat format = ResultDocument.FormatOf(path)!.Value;
        string whole = ResultDocument.Render(bytes, format);

        foreach (int at in Offsets(bytes.Length))
        {
            string split = ResultDocument.Render(ResultDocument.Split(bytes, at), format);
            if (!string.Equals(whole, split, StringComparison.Ordinal))
            {
                Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"{file} split at {at}/{bytes.Length}\n--- whole\n{whole}\n--- split\n{split}"));
            }
        }
    }

    /// <summary>
    /// Where a directory has the same test's result as .srx and .srj (the
    /// results-format suites do), both read to the same text.
    /// </summary>
    [Fact]
    public void The_same_result_in_xml_and_json_reads_the_same()
    {
        int compared = 0;
        foreach (string file in ResultDocument.Corpus())
        {
            if (!file.EndsWith(".srx", StringComparison.Ordinal))
            {
                continue;
            }

            string json = Path.ChangeExtension(Path.Combine(ResultDocument.SparqlSuites, file), ".srj");
            if (!File.Exists(json))
            {
                continue;
            }

            string xml = ResultDocument.Render(File.ReadAllBytes(Path.Combine(ResultDocument.SparqlSuites, file)), SparqlResultsFormat.Xml);
            Assert.Equal(xml, ResultDocument.Render(File.ReadAllBytes(json), SparqlResultsFormat.Json));
            compared++;
        }

        Assert.True(compared > 0, "No result exists in both formats; the comparison is vacuous.");
    }

    private static IEnumerable<int> Offsets(int length)
    {
        if (length <= EveryOffsetUpTo)
        {
            for (int at = 0; at <= length; at++)
            {
                yield return at;
            }

            yield break;
        }

        const int edge = 1024;
        int stride = Math.Max(1, length / 512);
        for (int at = 0; at <= edge; at++)
        {
            yield return at;
        }

        for (int at = edge + 1; at < length - edge; at += stride)
        {
            yield return at;
        }

        for (int at = Math.Max(edge + 1, length - edge); at <= length; at++)
        {
            yield return at;
        }
    }
}
