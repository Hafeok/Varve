// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Varve.Management.Model;
using Xunit;

namespace Varve.Management.Tests;

/// <summary>
/// A <c>varve apply</c> saved plan, format 1, as v0.1.0-preview.10 wrote it from
/// its published package (testdata/formats/plan-preview.10, ADR 0216). A plan
/// is versioned and positional, not read forever: this version reads format 1
/// as the release did and writes it back byte for byte, and refuses an unknown
/// format, telling the operator to plan again.
/// </summary>
public sealed class FormatFixtureTests
{
    [Fact]
    public void format_one_as_its_release_wrote_it_reads_graph_by_graph_and_writes_back()
    {
        string fixture = Fixture("plan-preview.10");
        byte[] bytes = File.ReadAllBytes(Path.Combine(fixture, "format-1.plan.json"));
        ManagementPlan plan = ManagementPlan.FromJson(bytes);
        Assert.Equal(File.ReadAllText(Path.Combine(fixture, "expectations", "plan.txt")), Describe(plan));
        Assert.Equal(bytes, plan.ToJson());
    }

    [Fact]
    public void an_unknown_format_is_refused_and_the_operator_told_to_plan_again()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(Fixture("plan-preview.10"), "format-1.plan.json"));
        byte[] later = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"format\": 1", "\"format\": 2", StringComparison.Ordinal));
        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => ManagementPlan.FromJson(later));
        Assert.Contains("of format 2", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Plan again with this version", refused.Message, StringComparison.Ordinal);
    }

    // As the fixture's generator describes the plan its release read.
    private static string Describe(ManagementPlan plan)
    {
        StringBuilder expected = new();
        expected.Append("position ").Append(plan.Position.Value).Append('\n');

        foreach (GraphPlan graph in plan.Graphs)
        {
            expected.Append("graph ").Append(Encoding.UTF8.GetString(graph.Graph.Lexical)).Append(" version ").Append(graph.Version.Value).Append('\n');

            foreach ((string name, IReadOnlyList<string> lines) in new[] { ("content", graph.Content), ("added", graph.Added), ("removed", graph.Removed) })
            {
                foreach (string line in lines)
                {
                    expected.Append("  ").Append(name).Append(' ').Append(line).Append('\n');
                }
            }
        }

        return expected.ToString();
    }

    private static string Fixture(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
        {
            directory = directory.Parent;
        }

        string fixture = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("No repository root above " + AppContext.BaseDirectory + "."), "testdata", "formats", name);

        foreach (string line in File.ReadAllLines(Path.Combine(fixture, "SHA256SUMS")).Where(l => l.Length > 0))
        {
            Assert.Equal(line[..64], Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture, line[66..])))));
        }

        return fixture;
    }
}
