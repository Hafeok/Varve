// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Shacl.Model;
using Varve.Shacl.Sparql;
using Varve.Turtle;
using Xunit;

namespace Varve.Learn.Tests;

/// <summary>
/// Every piece of the learning track's data conforms to the pinned REC 4
/// shapes (#94, ADR 0141): the hand-written slice, chapter by chapter, and
/// every sample's final state as its <c>expected/state.nq</c> holds it.
/// </summary>
public sealed class RecConformanceTests
{
    private static readonly ShaclOptions Options = new()
    {
        Extensions = [SparqlConstraints.Extension()],
        SubClassOfInShapesGraph = true,
        RefuseNonDeterministicFunctions = true,
    };

    private static readonly Lazy<ShapesGraph> Shapes = new(LoadShapes);

    /// <summary>The track pins the tag, and the tag's files are the ones it pinned.</summary>
    [Fact]
    public void The_pinned_rec_tag_is_the_vendored_one()
    {
        (string tag, string sums) = Pin();
        string directory = Path.Combine(LearnPaths.Root, "testdata", "rec", tag);
        Assert.True(Directory.Exists(directory), "samples/learn/rec.pin names " + tag + ", which testdata/rec/ does not hold.");
        string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "SHA256SUMS"))));
        Assert.True(sums == actual, "testdata/rec/" + tag + "/SHA256SUMS is " + actual + "; samples/learn/rec.pin pins " + sums + ".");
        foreach (string line in File.ReadAllLines(Path.Combine(directory, "SHA256SUMS")).Where(l => l.Length > 0))
        {
            string[] parts = line.Split("  ", 2);
            string file = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, parts[1]))));
            Assert.True(parts[0] == file, parts[1] + " is not the pinned file.");
        }
    }

    /// <summary>Each chapter's data, with the chapters before it, conforms.</summary>
    [Theory]
    [MemberData(nameof(SliceChapters))]
    public void The_slice_conforms_up_to_each_chapter(int chapter)
    {
        List<string> files = [.. Directory.EnumerateFiles(Path.Combine(LearnPaths.Samples, "data"), "*.trig")
            .Where(f => ChapterOf(f) <= chapter)
            .Order(StringComparer.Ordinal)];
        Assert.NotEmpty(files);
        InMemoryDatasetBuilder data = new();
        foreach (string file in files)
        {
            Read(file, RdfSyntax.TriG, data);
        }

        AssertConforms(data.ToDataset(), "the slice to chapter " + chapter);
    }

    /// <summary>Each sample's final state conforms.</summary>
    [Theory]
    [MemberData(nameof(SampleStates))]
    public void Each_samples_final_state_conforms(string sample)
    {
        InMemoryDatasetBuilder data = new();
        Read(Path.Combine(LearnPaths.Samples, sample, "expected", "state.nq"), RdfSyntax.NQuads, data);
        AssertConforms(data.ToDataset(), sample);
    }

    /// <summary>The chapters the slice has data for.</summary>
    public static TheoryData<int> SliceChapters() => [.. Directory.EnumerateFiles(Path.Combine(LearnPaths.Samples, "data"), "*.trig").Select(ChapterOf).Distinct().Order()];

    /// <summary>Every sample with a final state.</summary>
    public static TheoryData<string> SampleStates() => [.. Directory.EnumerateDirectories(LearnPaths.Samples)
        .Where(d => File.Exists(Path.Combine(d, "expected", "state.nq")))
        .Select(Path.GetFileName)
        .OfType<string>()
        .Order(StringComparer.Ordinal)];

    private static void AssertConforms(InMemoryDataset data, string what)
    {
        ValidationOutcome outcome = ShaclValidator.Validate(Shapes.Value, data, GraphPattern.Any, Options, TestContext.Current.CancellationToken);
        Assert.Empty(outcome.Problems);
        ValidationReport report = Assert.IsType<ValidationReport>(outcome.Report);
        Assert.True(report.Conforms, what + " does not conform to REC 4:\n" + string.Join("\n", report.Results.Select(Describe)));
    }

    private static string Describe(ShaclResult r) =>
        "  " + Text(r.FocusNode) + " " + Text(r.Path) + " " + Text(r.Value) + ": " + string.Join("; ", r.Messages.Select(Text));

    private static string Text(RdfTerm? term) => term is null ? "-" : Encoding.UTF8.GetString(term.Lexical);

    private static int ChapterOf(string file) => int.Parse(Path.GetFileName(file)[..2], System.Globalization.CultureInfo.InvariantCulture);

    private static (string Tag, string Sums) Pin()
    {
        Dictionary<string, string> pin = File.ReadAllLines(Path.Combine(LearnPaths.Samples, "rec.pin"))
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split(':', 2))
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.Ordinal);
        return (pin["tag"], pin["SHA256SUMS"]);
    }

    private static ShapesGraph LoadShapes()
    {
        (string tag, _) = Pin();
        string directory = Path.Combine(LearnPaths.Root, "testdata", "rec", tag);
        InMemoryDatasetBuilder shapes = new();
        string[] files =
        [
            Path.Combine(directory, "rec.ttl"),
            Path.Combine(directory, "Brick+patches.ttl"),
            Path.Combine(directory, "recimports.ttl"),
            Path.Combine(LearnPaths.Root, "testdata", "rec", "gate", "composition.ttl"),
        ];
        for (int f = 0; f < files.Length; f++)
        {
            Read(files[f], RdfSyntax.Turtle, shapes, "f" + f + "_");
        }

        ShapesLoad load = ShapesGraph.Load(shapes.ToDataset(), GraphPattern.DefaultGraph, Options, TestContext.Current.CancellationToken);
        Assert.Empty(load.Problems);
        return Assert.IsType<ShapesGraph>(load.Shapes);
    }

    /// <summary>Reads a file into <paramref name="into"/>, its blank nodes its own when <paramref name="blankPrefix"/> is given.</summary>
    private static void Read(string file, RdfSyntax syntax, InMemoryDatasetBuilder into, string? blankPrefix = null)
    {
        string prefix = blankPrefix ?? Path.GetFileNameWithoutExtension(file) + "_";
        RdfTerm Own(RdfTerm term) => term.Kind == RdfTermKind.BlankNode
            ? RdfTerm.BlankNode(Encoding.UTF8.GetBytes(prefix + Encoding.UTF8.GetString(term.Lexical)))
            : term;
        void Add(in QuadView q) => into.Add(
            Own(q.Subject.Materialise()),
            q.Predicate.Materialise(),
            Own(q.Object.Materialise()),
            q.HasGraph ? q.Graph.Materialise() : null);
        byte[] bytes = File.ReadAllBytes(file);
        ParseResult parsed = syntax == RdfSyntax.NQuads
            ? NQuadsParser.Parse(bytes, Add, new ParseOptions { Syntax = syntax })
            : TurtleParser.Parse(bytes, Add, new TurtleOptions { Syntax = syntax });
        Assert.True(parsed.Succeeded, file + ": " + parsed.FirstError);
    }
}
