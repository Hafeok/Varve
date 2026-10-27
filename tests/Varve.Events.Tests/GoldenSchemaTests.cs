// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Varve.Events.Model;
using Xunit;

namespace Varve.Events.Tests;

/// <summary>The schema generator's golden files: read for ever (ADR 0145, <c>GeneratorGoldenFiles</c>).</summary>
public sealed class GoldenSchemaTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "fixtures", "schemas");
    private static readonly EventEndpoints Endpoints = new("https://varve.example.org/", "golden");

    public static TheoryData<string> Cases => [.. Directory.GetDirectories(Root).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Cases))]
    public void every_revision_generates_its_golden_bytes(string name)
    {
        string folder = Path.Combine(Root, name);
        History history = new();
        List<Q> previous = [];

        foreach (string file in Directory.GetFiles(Path.Combine(folder, "history"), "*.trig").Where(f => char.IsAsciiDigit(Path.GetFileName(f)[0])).Order(StringComparer.Ordinal))
        {
            List<Q> state = Trig.Quads(File.ReadAllText(file));
            Assert.NotNull(history.Commit(previous.Except(state), state.Except(previous)));
            previous = state;
        }

        EventCatalogueHistory dataset = history.Catalogue(history.Head);
        EventCatalogueHistory catalogue = dataset;

        // A case that crosses the move (ADR 0167): mNN.trig is the catalogue,
        // in the catalogue graphs' form, at management position NN after it.
        string[] moved = [.. Directory.GetFiles(Path.Combine(folder, "history"), "m*.trig").Order(StringComparer.Ordinal)];

        if (moved.Length > 0)
        {
            catalogue = EventCatalogueHistory.ContinueFrom(dataset.Current);
            List<Q> managed = [];

            foreach (string file in moved)
            {
                List<Q> state = Trig.Quads(File.ReadAllText(file));
                List<Varve.Delta.Model.FeedChange> changes = [
                    .. managed.Except(state).Select(q => new Varve.Delta.Model.FeedChange(Varve.Delta.Model.FeedChangeKind.Retract, q.S, q.P, q.O, q.G)),
                    .. state.Except(managed).Select(q => new Varve.Delta.Model.FeedChange(Varve.Delta.Model.FeedChangeKind.Assert, q.S, q.P, q.O, q.G))];
                catalogue.Apply(new Varve.Delta.Model.FeedPosition(long.Parse(Path.GetFileNameWithoutExtension(file)[1..], System.Globalization.CultureInfo.InvariantCulture)), changes);
                managed = state;
            }
        }

        Dictionary<string, byte[]> generated = new(StringComparer.Ordinal);

        foreach (CataloguedType type in catalogue.Current.Types)
        {
            foreach (SchemaRevision revision in type.Revisions)
            {
                Assert.True((revision.Revision.IsManagement ? catalogue : dataset).At(revision.Revision).TryGet(type.Declaration.Name, out CataloguedType? then));
                Assert.Equal(revision.Version, then.Declaration.Version);
                string id = Endpoints.Schema(type.Declaration.Name, revision.Version, revision.Revision);
                generated[Path.Combine(type.Declaration.Name, "v" + revision.Version, "r" + revision.Revision + ".json")] =
                    EventSchemas.Generate(then.Declaration, id);
            }
        }

        string expected = Path.Combine(SourceFolder(name), "expected");

        if (Environment.GetEnvironmentVariable("VARVE_WRITE_GOLDEN") == "1" && !Directory.Exists(expected))
        {
            Assert.Null(Environment.GetEnvironmentVariable("CI"));

            foreach ((string path, byte[] bytes) in generated)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(expected, path))!);
                File.WriteAllBytes(Path.Combine(expected, path), bytes);
            }
        }

        string[] files = [.. Directory.GetFiles(Path.Combine(folder, "expected"), "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Path.Combine(folder, "expected"), f)).Order(StringComparer.Ordinal)];

        Assert.Equal(files, generated.Keys.Order(StringComparer.Ordinal));

        foreach (string file in files)
        {
            Assert.Equal(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder, "expected", file))), Encoding.UTF8.GetString(generated[file]));
        }
    }

    [Fact]
    public void the_evolving_case_has_the_revisions_the_registry_promises()
    {
        string[] revisions = [.. Directory.GetFiles(Path.Combine(Root, "evolving", "expected"), "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Path.Combine(Root, "evolving", "expected"), f).Replace('\\', '/')).Order(StringComparer.Ordinal)];

        // 1: declared. 2: an optional field. 3: a description. 4: version 2.
        // 5: the query's text alone, and 6: retirement, change no schema.
        Assert.Equal(["OrderPlaced/v1/r1.json", "OrderPlaced/v1/r2.json", "OrderPlaced/v1/r3.json", "OrderPlaced/v2/r4.json"], revisions);
    }

    [Fact]
    public void the_moved_case_keeps_its_revisions_across_the_move_and_mints_m_revisions_after()
    {
        string[] revisions = [.. Directory.GetFiles(Path.Combine(Root, "moved", "expected"), "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Path.Combine(Root, "moved", "expected"), f).Replace('\\', '/')).Order(StringComparer.Ordinal)];

        // 1: declared. 2: an optional field. The move at m5 changes no byte,
        // so it mints nothing; m7 adds an optional field, m9 is version 2.
        Assert.Equal(["OrderPlaced/v1/r1.json", "OrderPlaced/v1/r2.json", "OrderPlaced/v1/rm7.json", "OrderPlaced/v2/rm9.json"], revisions);
    }

    private static string SourceFolder(string name)
    {
        string? directory = AppContext.BaseDirectory;

        while (directory is not null && !File.Exists(Path.Combine(directory, "Varve.Events.Tests.csproj")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return Path.Combine(directory ?? throw new InvalidOperationException("The test project's folder is not above the output."), "fixtures", "schemas", name);
    }
}
