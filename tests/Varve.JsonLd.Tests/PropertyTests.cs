// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using CsCheck;
using Varve.Rdf;
using Varve.Turtle;
using Xunit;
using static Varve.JsonLd.Tests.Harness;

namespace Varve.JsonLd.Tests;

/// <summary>
/// The fromRdf writer against the toRdf reader over generated datasets (ADR
/// 0123, json-ld.md §6): write, read back, same dataset up to blank node
/// labels; write, read, write, same bytes. And the one dataset the writer
/// cannot express, which it refuses by name.
/// </summary>
public class PropertyTests
{
    private const string Xsd = "http://www.w3.org/2001/XMLSchema#";

    private static readonly Gen<string> Iri = Gen.Int[0, 5].Select(n => "<http://a.example/n" + n + ">");

    private static readonly Gen<string> Predicate = Gen.OneOf(
        Gen.Int[0, 4].Select(n => "<http://a.example/p" + n + ">"),
        Gen.Const("<http://www.w3.org/1999/02/22-rdf-syntax-ns#type>"));

    private static readonly Gen<string> Blank = Gen.Int[0, 3].Select(n => "_:b" + n);

    private static readonly Gen<string> Graph = Gen.OneOf(Gen.Const(""), Gen.Int[0, 2].Select(n => " <http://a.example/g" + n + ">"));

    private static readonly Gen<string> Text = Gen.String[Gen.OneOf(Gen.Char.AlphaNumeric, Gen.Const(' '), Gen.Const('"'), Gen.Const('\\'), Gen.Const('\n'), Gen.Const('é'), Gen.Const('中')), 0, 12];

    private static readonly Gen<string> Literal = Gen.OneOf(
        Text.Select(t => Quote(t)),
        Text.Select(t => Quote(t) + "@en"),
        Text.Select(t => Quote(t) + "@ar--rtl"),
        Text.Select(t => Quote(t) + "@en-GB--ltr"),
        Gen.Int.Select(i => "\"" + i + "\"^^<" + Xsd + "integer>"),
        Gen.Const("\"1.5E0\"^^<" + Xsd + "double>"),
        Gen.Const("\"true\"^^<" + Xsd + "boolean>"),
        Gen.Const("\"{\\\"a\\\":[1,true]}\"^^<http://www.w3.org/1999/02/22-rdf-syntax-ns#JSON>"),
        Text.Select(t => Quote(t) + "^^<http://a.example/dt>"));

    private static readonly Gen<string> Node = Gen.OneOf(Iri, Blank);

    private static readonly Gen<string> Quad = Gen.Select(Node, Predicate, Gen.OneOf(Iri, Blank, Literal), Graph).Select(t => t.Item1 + " " + t.Item2 + " " + t.Item3 + t.Item4 + " .");

    private static readonly Gen<string> Dataset = Quad.List[0, 12].Select(lines => string.Join('\n', lines) + "\n");

    [Fact]
    public void a_dataset_survives_the_writer_and_the_reader_as_the_same_dataset()
    {
        Dataset.Sample(
            nquads =>
            {
                List<string> before = ReadNQuads(nquads);
                string json = FromRdf(nquads, new JsonLdWriteOptions { Indent = false });
                Read back = ToRdf(json, baseIri: null);

                if (!back.Result.Succeeded)
                {
                    throw new InvalidOperationException(back.Result.Error!.ToString() + "\n" + json);
                }

                return Same(before, back.Lines);
            },
            iter: 2000);
    }

    [Fact]
    public void writing_what_was_written_reproduces_it()
    {
        Dataset.Sample(
            nquads =>
            {
                // Up to blank node labels: toRdf renumbers them (ADR 0123,
                // BlankNodesAreRelabelled), so the second document names
                // the same nodes differently and nothing else.
                string first = FromRdf(nquads, new JsonLdWriteOptions { Indent = false });
                Read back = ToRdf(first, baseIri: null);
                string second = FromRdf(string.Join('\n', back.Lines.ConvertAll(l => l + " .")) + "\n", new JsonLdWriteOptions { Indent = false });
                return string.Equals(NumberBlankLabels(first), NumberBlankLabels(second), StringComparison.Ordinal);
            },
            iter: 1000);
    }

    [Fact]
    public void a_triple_term_is_refused_by_name()
    {
        Gen.Select(Node, Predicate, Gen.Select(Node, Predicate, Gen.OneOf(Iri, Literal))).Sample(
            t =>
            {
                string nquads = t.Item1 + " " + t.Item2 + " <<( " + t.Item3.Item1 + " " + t.Item3.Item2 + " " + t.Item3.Item3 + " )>> .\n";
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => FromRdf(nquads, default));
                return error.Message.Contains("triple term", StringComparison.Ordinal);
            },
            iter: 200);
    }

    private static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    private static List<string> ReadNQuads(string nquads)
    {
        List<string> lines = [];
        ParseResult result = NQuadsParser.Parse(U(nquads), (in QuadView quad) => lines.Add(Line(in quad)), new ParseOptions { Syntax = RdfSyntax.NQuads });

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.FirstError.ToString());
        }

        return lines;
    }

    /// <summary>
    /// Whether two runs denote the same dataset: equal as sets of quads under
    /// some bijection of blank node labels. The datasets hold at most a
    /// handful of blank nodes, so every bijection is tried; that is exact
    /// where a relabelling by first appearance is not, because the writer
    /// sorts subjects and the reader numbers nodes in document order.
    /// </summary>
    private static bool Same(List<string> left, List<string> right)
    {
        // Language tags are compared without regard to case: the processor
        // lowercases them (ADR 0123, LanguageTagsAreLowercased).
        HashSet<string> leftSet = new(left.ConvertAll(LowercaseTag), StringComparer.Ordinal);
        HashSet<string> rightSet = new(right.ConvertAll(LowercaseTag), StringComparer.Ordinal);
        List<string> leftLabels = Labels(leftSet);
        List<string> rightLabels = Labels(rightSet);

        if (leftLabels.Count != rightLabels.Count)
        {
            return false;
        }

        foreach (List<string> permutation in Permutations(rightLabels))
        {
            Dictionary<string, string> map = new(StringComparer.Ordinal);

            for (int i = 0; i < leftLabels.Count; i++)
            {
                map[leftLabels[i]] = permutation[i];
            }

            HashSet<string> relabelled = new(StringComparer.Ordinal);

            foreach (string line in leftSet)
            {
                relabelled.Add(Relabel(line, map));
            }

            if (relabelled.SetEquals(rightSet))
            {
                return true;
            }
        }

        return false;
    }

    private static string LowercaseTag(string line) =>
        System.Text.RegularExpressions.Regex.Replace(line, "\"@([A-Za-z0-9-]+)", m => "\"@" + m.Groups[1].Value.ToLowerInvariant());

    /// <summary>Blank node labels numbered by first appearance, so two labellings of one document compare equal.</summary>
    internal static string NumberBlankLabels(string json)
    {
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        return System.Text.RegularExpressions.Regex.Replace(json, "_:[A-Za-z0-9]+", m =>
        {
            if (!seen.TryGetValue(m.Value, out int index))
            {
                index = seen.Count;
                seen[m.Value] = index;
            }

            return "_:n" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        });
    }

    private static List<string> Labels(HashSet<string> lines)
    {
        SortedSet<string> labels = new(StringComparer.Ordinal);

        foreach (string line in lines)
        {
            foreach (string token in line.Split(' '))
            {
                if (token.StartsWith("_:", StringComparison.Ordinal))
                {
                    labels.Add(token);
                }
            }
        }

        return [.. labels];
    }

    private static IEnumerable<List<string>> Permutations(List<string> items)
    {
        if (items.Count <= 1)
        {
            yield return items;
            yield break;
        }

        for (int i = 0; i < items.Count; i++)
        {
            List<string> rest = [.. items];
            string head = rest[i];
            rest.RemoveAt(i);

            foreach (List<string> tail in Permutations(rest))
            {
                yield return [head, .. tail];
            }
        }
    }

    private static string Relabel(string line, Dictionary<string, string> map)
    {
        string[] tokens = line.Split(' ');

        for (int i = 0; i < tokens.Length; i++)
        {
            if (map.TryGetValue(tokens[i], out string? label))
            {
                tokens[i] = label;
            }
        }

        return string.Join(' ', tokens);
    }
}
