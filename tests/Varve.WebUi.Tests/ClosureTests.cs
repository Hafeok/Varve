// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// No <c>src/</c> package gains a Blazor reference (ADR 0200): every project
/// under <c>src/</c> but the Web UI itself has no
/// <c>Microsoft.AspNetCore.Components</c> package anywhere in its restore
/// graph, directly or through what it references. The register's scope refuses
/// a direct reference; this refuses one that arrives by any path.
/// </summary>
public class ClosureTests
{
    [Fact]
    public void No_project_under_src_but_the_web_ui_has_blazor_in_its_closure()
    {
        string root = RepositoryRoot();
        List<string> checkedProjects = [];
        List<string> findings = [];

        foreach (string project in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(project);
            string assets = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");

            if (name == "Varve.WebUi" || !File.Exists(assets))
            {
                continue;
            }

            checkedProjects.Add(name);
            using JsonDocument graph = JsonDocument.Parse(File.ReadAllBytes(assets));

            foreach (JsonProperty library in graph.RootElement.GetProperty("libraries").EnumerateObject())
            {
                if (library.Name.StartsWith("Microsoft.AspNetCore.Components", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(name + " → " + library.Name);
                }
            }
        }

        // The restore that every build runs first wrote these; a check over
        // nothing would pass, so it insists on the projects it knows.
        Assert.Contains("Varve.Protocol.Client", checkedProjects);
        Assert.Contains("Varve.Server", checkedProjects);
        Assert.Empty(findings);
    }

    [Fact]
    public void The_web_ui_itself_has_blazor_in_its_closure()
    {
        string assets = Path.Combine(RepositoryRoot(), "src", "Varve.WebUi", "obj", "project.assets.json");
        using JsonDocument graph = JsonDocument.Parse(File.ReadAllBytes(assets));
        Assert.Contains(graph.RootElement.GetProperty("libraries").EnumerateObject(), library => library.Name.StartsWith("Microsoft.AspNetCore.Components.WebAssembly/", StringComparison.Ordinal));
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
