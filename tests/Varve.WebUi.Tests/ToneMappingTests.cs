// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Varve.Protocol.Model;
using Varve.WebUi.Atoms;
using Xunit;
using ClientModel = Varve.Protocol.Client.Model;

namespace Varve.WebUi.Tests;

/// <summary>
/// The one state-to-tone mapping (ADR 0206 point 5): every problem type the
/// server can answer and every state word the API and the brand book document
/// has a tone, so a new one fails here before it renders neutral by default.
/// </summary>
public class ToneMappingTests
{
    [Fact]
    public void every_problem_type_and_state_word_has_a_tone()
    {
        List<string> missing = [];

        foreach (ProblemShape shape in ProblemCatalogue.All)
        {
            string name = shape.Type.Value[ProblemType.Namespace.Length..];

            if (!Tones.TryOfProblem(name, out _))
            {
                missing.Add("problem " + name);
            }
        }

        // docs/problems/ documents types the catalogue does not answer yet
        // (Replication's writer-superseded, among them); they have tones too.
        foreach (string page in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "docs", "problems"), "*.md"))
        {
            string name = Path.GetFileNameWithoutExtension(page);

            if (name != "README" && !Tones.TryOfProblem(name, out _))
            {
                missing.Add("problem page " + name);
            }
        }

        foreach (string word in StateWords())
        {
            if (!Tones.TryOfState(word, out _))
            {
                missing.Add("state " + word);
            }
        }

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("open", Tone.Ok)]
    [InlineData("writer", Tone.Ok)]
    [InlineData("behind", Tone.Warn)]
    [InlineData("failed", Tone.Critical)]
    [InlineData("superseded", Tone.Critical)]
    [InlineData("mismatch", Tone.Critical)]
    [InlineData("closed", Tone.Neutral)]
    [InlineData("follower", Tone.Neutral)]
    [InlineData("elsewhere", Tone.Neutral)]
    [InlineData("anonymous", Tone.Neutral)]
    public void a_state_word_has_the_tone_the_brand_book_gives_it(string word, Tone tone) =>
        Assert.Equal(tone, Tones.OfState(word));

    [Fact]
    public void an_unknown_word_is_neutral_and_an_unknown_problem_critical()
    {
        Assert.False(Tones.TryOfState("warm-ish", out _));
        Assert.Equal(Tone.Neutral, Tones.OfState("warm-ish"));
        Assert.Equal(Tone.Critical, Tones.OfProblem("a-type-from-a-newer-server"));
        Assert.Equal(Tone.Warn, Tones.OfProblem("management-behind"));
    }

    // The state words the client names as static members of its wrappers
    // (ADR 0203), and the backticked words of the brand book's tone table.
    private static IEnumerable<string> StateWords()
    {
        foreach (Type wrapper in new[] { typeof(ClientModel.InstanceRole), typeof(ClientModel.ReadinessState), typeof(ClientModel.BootstrapState) })
        {
            foreach (PropertyInfo property in wrapper.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                yield return property.GetValue(null)!.ToString()!;
            }
        }

        string book = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "ui", "README.md"));
        string table = book[book.IndexOf("| Tone | Tokens | Mark | Words from the API |", StringComparison.Ordinal)..];
        table = table[..table.IndexOf("\n\n", StringComparison.Ordinal)];

        foreach (string row in table.Split('\n').Skip(2))
        {
            string words = row.Split('|')[4];

            foreach (Match word in Regex.Matches(words, "`([a-z-]+)`"))
            {
                yield return word.Groups[1].Value;
            }
        }
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("No Varve.slnx above " + AppContext.BaseDirectory + ".");
    }
}
