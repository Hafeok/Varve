// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Varve.Sparql.Algebra;
using Varve.WebUi.Organisms;
using Varve.WebUi.Parts;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// The query editor colours with ADR 0248's tokenizer: every <c>QueryEditor</c>
/// case's runs are exactly what <see cref="QueryColouring"/> gives for its
/// text, so a case can't draw a colouring the editor would never produce.
/// </summary>
public class QueryColouringTests
{
    public static TheoryData<string> Cases()
    {
        TheoryData<string> data = new();

        foreach (string json in Directory.GetFiles(CasesFolder(), "*.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileNameWithoutExtension(json));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void a_cases_runs_are_the_tokenizers(string name)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(CasesFolder(), name + ".json")));
        JsonElement parameters = document.RootElement.GetProperty("parameters");
        string text = parameters.GetProperty("Text").GetString()!;
        List<EditorRun> drawn = parameters.GetProperty("Runs").Deserialize<List<EditorRun>>()!;

        Assert.Equal(QueryColouring.Runs(text), drawn);
    }

    [Fact]
    public void every_token_kind_has_a_class()
    {
        string[] classes = ["keyword", "iri", "bnode", "variable", "literal", "annotation", "punct", "comment", "word"];

        foreach (SparqlTokenKind kind in Enum.GetValues<SparqlTokenKind>())
        {
            Assert.Contains(QueryColouring.Kind(kind), classes);
        }
    }

    private static string CasesFolder()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return Path.Combine(directory.FullName, "docs", "ui", "components", "QueryEditor", "cases");
            }
        }

        throw new InvalidOperationException("No Varve.slnx above " + AppContext.BaseDirectory + ".");
    }
}
