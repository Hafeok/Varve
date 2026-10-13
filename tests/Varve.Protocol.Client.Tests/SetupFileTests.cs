// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>
/// The setup file (ADR 0130): the YAML subset <c>docs/operator/datasets.md</c>
/// specifies, read to the setup it declares, and every construct outside it
/// refused at its line and column.
/// </summary>
public class SetupFileTests
{
    private const string Full = """
        # The shop's orders dataset.
        dataset: orders
        storage: Memory
        settings: |
          {"defaultAccessScope": "Current"}

        baseline:
          - name: vocabulary
            graph: https://shop.example/vocabulary   # its own graph
            file: vocabulary.ttl
          - file: statuses.nt
            graph: default
          - file: shapes.data
            format: 'application/ld+json'

        migrations:
          - id: 2026-10-01-statuses
            update: |
              DELETE WHERE { ?order <https://shop.example/ns#state> ?s } ;
              INSERT DATA { <https://shop.example/x> <https://shop.example/ns#y> "a # not a comment" }
          - id: "2026-10-02-rename \"quoted\""
            file: rename.ru
        """;

    [Fact]
    public async Task A_setup_file_reads_to_the_setup_it_declares()
    {
        string directory = Directory.CreateTempSubdirectory("varve-setup-").FullName;

        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "vocabulary.ttl"), "<http://ex/a> <http://ex/p> 1 .", C.Ct);
            await File.WriteAllTextAsync(Path.Combine(directory, "rename.ru"), "INSERT DATA { <http://ex/r> <http://ex/p> 2 }", C.Ct);
            await File.WriteAllTextAsync(Path.Combine(directory, "setup.yaml"), Full, C.Ct);

            DatasetSetup setup = DatasetSetupFile.Read(Path.Combine(directory, "setup.yaml"));

            Assert.Equal("orders", setup.Name);
            Assert.Equal(SetupStorage.Memory, setup.Storage);
            Assert.Equal("{\"defaultAccessScope\": \"Current\"}\n", setup.Settings);
            Assert.Equal(3, setup.Baselines.Count);
            Assert.Equal(("vocabulary", "https://shop.example/vocabulary", "text/turtle"), (setup.Baselines[0].Name, setup.Baselines[0].Graph, setup.Baselines[0].MediaType));
            Assert.Equal(("statuses.nt", (string?)null, "application/n-triples"), (setup.Baselines[1].Name, setup.Baselines[1].Graph, setup.Baselines[1].MediaType));
            Assert.Equal("application/ld+json", setup.Baselines[2].MediaType);
            Assert.Equal("<http://ex/a> <http://ex/p> 1 .", Encoding.UTF8.GetString(await setup.Baselines[0].ReadAsync(C.Ct)));
            Assert.Equal("2026-10-01-statuses", setup.Migrations[0].Id);
            Assert.Equal(
                "DELETE WHERE { ?order <https://shop.example/ns#state> ?s } ;\nINSERT DATA { <https://shop.example/x> <https://shop.example/ns#y> \"a # not a comment\" }\n",
                setup.Migrations[0].Update);
            Assert.Equal("2026-10-02-rename \"quoted\"", setup.Migrations[1].Id);
            Assert.Equal("INSERT DATA { <http://ex/r> <http://ex/p> 2 }", setup.Migrations[1].Update);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("dataset: orders\n\tstorage: File\n", 2, 1, "a tab")]
    [InlineData("dataset: &name orders\n", 1, 10, "an anchor")]
    [InlineData("dataset: orders\nstorage: *name\n", 2, 10, "an alias")]
    [InlineData("dataset: !!str orders\n", 1, 10, "a tag")]
    [InlineData("dataset: orders\nbaseline: [a, b]\n", 2, 11, "a flow collection")]
    [InlineData("dataset: orders\nsettings: {a: 1}\n", 2, 11, "a flow collection")]
    [InlineData("dataset: orders\nsettings: >\n  folded\n", 2, 11, "folded")]
    [InlineData("dataset: orders\nsettings: |-\n  x\n", 2, 12, "chomping")]
    [InlineData("---\ndataset: orders\n", 1, 1, "document marker")]
    [InlineData("dataset: orders\ndataset: other\n", 2, 1, "given twice")]
    [InlineData("dataset: orders\nretention: forever\n", 2, 1, "'retention' is not a key here")]
    [InlineData("dataset: orders\nbaseline:\n  - file: a.ttl\n    weight: 3\n", 4, 5, "'weight' is not a key here")]
    [InlineData("dataset: orders\nbaseline:\n  - file: a.ttl\n    mode: replace\n", 4, 5, "'mode' is not a key here")]
    [InlineData("dataset: orders\nstorage: Tape\n", 2, 10, "storage is File or Memory")]
    [InlineData("dataset: orders\nmigrations:\n  - id: 1\n", 3, 5, "update or file")]
    [InlineData("\"dataset\": orders\n", 1, 1, "a key is plain")]
    [InlineData("dataset:orders\n", 1, 8, "followed by ': '")]
    [InlineData("dataset: 'orders\n", 1, 10, "single-quoted")]
    [InlineData("dataset: \"or\\qders\"\n", 1, 13, "not an escape")]
    [InlineData("dataset: orders\n  storage: File\n", 2, 3, "indented more")]
    [InlineData("storage: File\n", 1, 1, "'dataset' is required")]
    [InlineData("", 1, 1, "empty")]
    public void What_is_outside_the_subset_is_refused_at_its_line_and_column(string text, int line, int column, string says)
    {
        SetupFileException refused = Assert.Throws<SetupFileException>(() => DatasetSetupFile.Parse(text, Path.GetTempPath(), "setup.yaml"));

        Assert.Contains(says, refused.Message, StringComparison.Ordinal);
        Assert.Equal((line, column), (refused.Line, refused.Column));
        Assert.StartsWith("setup.yaml:" + line + ":" + column + ": ", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_whose_format_is_not_known_from_its_extension_names_format()
    {
        SetupFileException refused = Assert.Throws<SetupFileException>(() =>
            DatasetSetupFile.Parse("dataset: orders\nbaseline:\n  - file: data.bin\n", Path.GetTempPath(), "setup.yaml"));
        Assert.Contains("give format", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_read_twice_reads_the_baseline_again()
    {
        string directory = Directory.CreateTempSubdirectory("varve-setup-").FullName;

        try
        {
            string file = Path.Combine(directory, "v.ttl");
            await File.WriteAllTextAsync(file, "<http://ex/a> <http://ex/p> 1 .", C.Ct);
            DatasetSetup setup = DatasetSetupFile.Parse("dataset: d\nbaseline:\n  - file: v.ttl\n", directory);
            await File.WriteAllTextAsync(file, "<http://ex/a> <http://ex/p> 2 .", C.Ct);
            Assert.Equal("<http://ex/a> <http://ex/p> 2 .", Encoding.UTF8.GetString(await setup.Baselines[0].ReadAsync(C.Ct)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
