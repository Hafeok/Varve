// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Varve.Rec.Tests;

/// <summary>
/// The learning track's generated portfolio (#94, #411, ADR 0141 as amended
/// on 2026-11-25): REC S at its head, cut to the first two buildings of site 0
/// and flattened into the default graph, written to
/// <c>samples/learn/data/03-portfolio.trig</c>. Samples reference nothing
/// internal, so they load this file rather than run the generator; the file is
/// the generator's output, and this test holds it to that. Leases and their
/// companies, and the site's owner, are left out: agents and collections are
/// chapter 8's.
/// </summary>
public sealed class LearnFixtureTests
{
    /// <summary>The fixture's SHA-256, pinned: a change to the generator that moves it is seen here.</summary>
    private const string Pinned = "6d2d5ec3d8e1fb41b43ecb1266f1d32b1a17633a067fade601040e0f4d600e0e";

    private const string Data = "https://example.org/rec/";
    private const string HasPart = "<https://w3id.org/rec#hasPart>";
    private const string IsLocationOf = "<https://w3id.org/rec#isLocationOf>";
    private const string HasPoint = "<https://w3id.org/rec#hasPoint>";
    private const string Type = "<http://www.w3.org/1999/02/22-rdf-syntax-ns#type>";
    private const string Label = "<http://www.w3.org/2000/01/rdf-schema#label>";

    /// <summary>The kinds of entity the fixture keeps, in the order it writes them.</summary>
    private static readonly string[] Kinds = ["region", "site", "building", "level", "room", "asset", "point"];

    [Fact]
    public void the_learning_tracks_portfolio_is_the_generators_cut()
    {
        string generated = Write(Cut(Head()));
        string path = Path.Combine(Root(), "samples", "learn", "data", "03-portfolio.trig");
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(generated)));
        TestContext.Current.TestOutputHelper?.WriteLine("03-portfolio.trig: " + generated.Length + " bytes, SHA-256 " + hash);

        if (Environment.GetEnvironmentVariable("VARVE_WRITE_GOLDEN") is not null)
        {
            File.WriteAllText(path, generated);
            return;
        }

        Assert.True(File.Exists(path), path + " does not exist; VARVE_WRITE_GOLDEN=1 writes it.");
        Assert.True(File.ReadAllText(path) == generated, "samples/learn/data/03-portfolio.trig is not the generator's cut; VARVE_WRITE_GOLDEN=1 writes it again.");
        Assert.Equal(Pinned, hash);
    }

    /// <summary>REC S's head, as quads, graphs dropped.</summary>
    private static HashSet<(string S, string P, string O)> Head()
    {
        HashSet<RecQuad> head = [];
        foreach (RecCommit commit in RecPortfolio.Generate(RecSize.S, RecPortfolioTests.Seed))
        {
            foreach (RecQuad q in commit.Retracted)
            {
                head.Remove(q);
            }

            foreach (RecQuad q in commit.Asserted)
            {
                head.Add(q);
            }
        }

        return [.. head.Select(q => (q.Subject, q.Predicate, q.Object))];
    }

    /// <summary>Region 0, site 0, buildings 0 and 1, and everything below them by hasPart, isLocationOf and hasPoint.</summary>
    private static List<(string S, string P, string O)> Cut(HashSet<(string S, string P, string O)> head)
    {
        Dictionary<string, List<string>> down = [];
        foreach ((string s, string p, string o) in head)
        {
            if (p is HasPart or IsLocationOf or HasPoint)
            {
                if (!down.TryGetValue(s, out List<string>? children))
                {
                    down[s] = children = [];
                }

                children.Add(o);
            }
        }

        HashSet<string> keep = [Entity("region", 0), Entity("site", 0)];
        Queue<string> next = new([Entity("building", 0), Entity("building", 1)]);
        while (next.TryDequeue(out string? node))
        {
            if (keep.Add(node) && down.TryGetValue(node, out List<string>? children))
            {
                children.ForEach(next.Enqueue);
            }
        }

        return [.. head.Where(q => keep.Contains(q.S) && (!IsEntity(q.O) || keep.Contains(q.O)))];
    }

    /// <summary>TriG in the default graph: one block per subject, in kind and number order; within it rdf:type, rdfs:label, then the rest by predicate and object.</summary>
    private static string Write(List<(string S, string P, string O)> quads)
    {
        StringBuilder text = new();
        text.Append("# The learning track's generated portfolio (#94): REC S at its head,\n");
        text.Append("# seed 20261015, cut to buildings 0 and 1 of site 0, graphs dropped.\n");
        text.Append("# Written by tests/Varve.Rec.Tests/LearnFixtureTests.cs; never edited by hand.\n");
        text.Append("PREFIX rec:   <https://w3id.org/rec#>\n");
        text.Append("PREFIX brick: <https://brickschema.org/schema/Brick#>\n");
        text.Append("PREFIX rdfs:  <http://www.w3.org/2000/01/rdf-schema#>\n");
        foreach (string kind in Kinds)
        {
            text.Append("PREFIX ").Append((kind + ":").PadRight(10)).Append('<').Append(Data).Append(kind).Append("/>\n");
        }

        foreach (IGrouping<string, (string S, string P, string O)> subject in quads.GroupBy(q => q.S).OrderBy(g => Order(g.Key)))
        {
            text.Append('\n').Append(Name(subject.Key));
            List<(string P, string O)> lines = [.. subject.Select(q => (q.P, q.O)).OrderBy(l => Rank(l.P)).ThenBy(l => l.P, StringComparer.Ordinal).ThenBy(l => Order(l.O))];
            for (int i = 0; i < lines.Count; i++)
            {
                text.Append(i == 0 ? " " : " ;\n  ").Append(lines[i].P == Type ? "a" : Name(lines[i].P)).Append(' ').Append(Name(lines[i].O));
            }

            text.Append(" .\n");
        }

        return text.ToString();
    }

    private static int Rank(string predicate) => predicate == Type ? 0 : predicate == Label ? 1 : 2;

    /// <summary>An entity's place: its kind's index, then its number; anything else after, by its text.</summary>
    private static (int Kind, int Number, string Text) Order(string term)
    {
        Match m = Regex.Match(term, "^<" + Regex.Escape(Data) + "([a-z]+)/([0-9]+)>$");
        return m.Success
            ? (Array.IndexOf(Kinds, m.Groups[1].Value), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), "")
            : (Kinds.Length, 0, term);
    }

    private static string Name(string term)
    {
        Match entity = Regex.Match(term, "^<" + Regex.Escape(Data) + "([a-z]+)/([0-9]+)>$");
        if (entity.Success)
        {
            return entity.Groups[1].Value + ":" + entity.Groups[2].Value;
        }

        foreach ((string prefix, string ns) in (ReadOnlySpan<(string, string)>)[("rec:", "https://w3id.org/rec#"), ("brick:", "https://brickschema.org/schema/Brick#"), ("rdfs:", "http://www.w3.org/2000/01/rdf-schema#")])
        {
            if (term.StartsWith("<" + ns, StringComparison.Ordinal))
            {
                return prefix + term[(ns.Length + 1)..^1];
            }
        }

        return term;
    }

    private static string Entity(string kind, int number) => "<" + Data + kind + "/" + number.ToString(CultureInfo.InvariantCulture) + ">";

    private static bool IsEntity(string term) => term.StartsWith("<" + Data, StringComparison.Ordinal);

    private static string Root()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root was not found above " + AppContext.BaseDirectory + ".");
    }
}
