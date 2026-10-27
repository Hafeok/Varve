// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// The markup contract (ADR 0206 point 6, ADR 0204): every component under
/// <c>docs/ui/components/</c> renders, for each of its cases, the markup the
/// case states.
/// </summary>
/// <remarks>
/// <para>
/// A case is two files in the component's <c>cases/</c> folder:
/// <c>&lt;case&gt;.json</c>, the parameters, and <c>&lt;case&gt;.html</c>, the
/// expected fragment. In the parameters, a string for a
/// <see cref="RenderFragment"/> is markup, a string for an enum is the
/// member's name, and anything else is read as JSON into the parameter's type.
/// </para>
/// <para>
/// The comparison is bUnit's semantic one: attribute order and insignificant
/// whitespace don't count; an element, a class or an attribute does.
/// </para>
/// </remarks>
public class ComponentContractTests
{
    private static readonly string[] Levels = ["Atoms", "Molecules", "Organisms", "Templates"];

    public static TheoryData<string, string> Cases()
    {
        TheoryData<string, string> data = new();

        foreach (string component in Directory.GetDirectories(ComponentsRoot()).Select(Path.GetFileName).Order(StringComparer.Ordinal)!)
        {
            string cases = Path.Combine(ComponentsRoot(), component!, "cases");

            if (!Directory.Exists(cases))
            {
                continue;
            }

            foreach (string json in Directory.GetFiles(cases, "*.json").Order(StringComparer.Ordinal))
            {
                data.Add(component!, Path.GetFileNameWithoutExtension(json));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void the_component_renders_the_case(string component, string name)
    {
        string folder = Path.Combine(ComponentsRoot(), component, "cases");
        Type type = ComponentType(component)
            ?? throw new InvalidOperationException($"docs/ui/components/{component} has no component of that name in a level namespace.");

        using JsonDocument parameters = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, name + ".json")));
        string expected = File.ReadAllText(Path.Combine(folder, name + ".html"));

        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        IRenderedComponent<ContainerFragment> rendered = context.Render(builder =>
        {
            builder.OpenComponent(0, type);
            int sequence = 1;

            foreach (JsonProperty parameter in parameters.RootElement.GetProperty("parameters").EnumerateObject())
            {
                PropertyInfo property = type.GetProperty(parameter.Name)
                    ?? throw new InvalidOperationException($"{component} has no parameter {parameter.Name} ({name}.json).");
                builder.AddAttribute(sequence++, parameter.Name, Value(property.PropertyType, parameter.Value));
            }

            builder.CloseComponent();
        });

        rendered.MarkupMatches(expected, $"docs/ui/components/{component}/cases/{name}");
    }

    [Fact]
    public void every_component_with_a_preview_has_cases_and_a_component()
    {
        List<string> missing = [];

        foreach (string folder in Directory.GetDirectories(ComponentsRoot()))
        {
            string component = Path.GetFileName(folder);

            if (!File.Exists(Path.Combine(folder, "preview.html")))
            {
                continue;
            }

            if (ComponentType(component) is null)
            {
                missing.Add(component + ": no component");
            }

            string cases = Path.Combine(folder, "cases");

            if (!Directory.Exists(cases) || Directory.GetFiles(cases, "*.json").Length == 0)
            {
                missing.Add(component + ": no cases");
            }
            else
            {
                missing.AddRange(Directory.GetFiles(cases, "*.json")
                    .Where(json => !File.Exists(Path.ChangeExtension(json, ".html")))
                    .Select(json => component + ": " + Path.GetFileName(json) + " has no .html"));
            }
        }

        Assert.True(missing.Count == 0, "Every component in docs/ui has a component and contract cases (ADR 0206 point 6): " + string.Join("; ", missing));
    }

    private static object? Value(Type type, JsonElement value)
    {
        if (type == typeof(RenderFragment))
        {
            string markup = value.GetString() ?? string.Empty;
            return (RenderFragment)(builder => builder.AddMarkupContent(0, markup));
        }

        Type plain = Nullable.GetUnderlyingType(type) ?? type;

        if (plain.IsEnum)
        {
            return Enum.Parse(plain, value.GetString()!, ignoreCase: false);
        }

        return value.Deserialize(type);
    }

    private static Type? ComponentType(string name) =>
        Levels
            .Select(level => typeof(Program).Assembly.GetType($"Varve.WebUi.{level}.{name}"))
            .FirstOrDefault(type => type is not null && typeof(IComponent).IsAssignableFrom(type));

    private static string ComponentsRoot() => Path.Combine(RepositoryRoot(), "docs", "ui", "components");

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "Varve.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (Varve.slnx) is not above the test's output.");
    }
}
