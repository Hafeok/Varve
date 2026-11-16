// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Varve.WebUi.Molecules;
using Varve.WebUi.Organisms;
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
/// A case whose markup is only valid inside another element, such as a table
/// row, names it: <c>"within": "tbody"</c> beside <c>parameters</c> renders
/// and compares the case inside <c>&lt;table&gt;&lt;tbody&gt;</c>, since an
/// HTML parser drops a row found anywhere else.
/// </para>
/// <para>
/// The comparison is bUnit's semantic one, with whitespace preserved on both
/// sides: attribute order doesn't count; an element, a class, an attribute or
/// a space between two inline elements does.
/// </para>
/// <para>
/// Each preview carries its cases verbatim, one per <c>data-case</c> element,
/// compared the same way. The one mapping is the brand image: a preview
/// reaches it at <c>../../assets/</c>, the app at <c>img/</c>, where the
/// project links it.
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

        using BunitContext context = NewContext(TimeProvider.System);
        string[] within = Within(parameters);

        IRenderedComponent<ContainerFragment> rendered = context.Render(builder =>
        {
            builder.OpenElement(0, within[0]);
            builder.AddAttribute(1, "diff:whitespace", "preserve");

            for (int i = 1; i < within.Length; i++)
            {
                builder.OpenElement(1, within[i]);
            }

            builder.OpenComponent(1, type);

            foreach (JsonProperty parameter in parameters.RootElement.GetProperty("parameters").EnumerateObject())
            {
                PropertyInfo property = type.GetProperty(parameter.Name)
                    ?? throw new InvalidOperationException($"{component} has no parameter {parameter.Name} ({name}.json).");
                builder.AddAttribute(2, parameter.Name, Value(property.PropertyType, parameter.Value));
            }

            builder.CloseComponent();

            foreach (string _ in within)
            {
                builder.CloseElement();
            }
        });

        rendered.MarkupMatches(Strict(expected, within), $"docs/ui/components/{component}/cases/{name}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void the_preview_shows_the_case(string component, string name)
    {
        string expected = File.ReadAllText(Path.Combine(ComponentsRoot(), component, "cases", name + ".html"));
        using JsonDocument parameters = JsonDocument.Parse(File.ReadAllText(Path.Combine(ComponentsRoot(), component, "cases", name + ".json")));
        string[] within = Within(parameters);
        IElement shown = PreviewCases(component).FirstOrDefault(element => element.GetAttribute("data-case") == name)
            ?? throw new InvalidOperationException($"docs/ui/components/{component}/preview.html has no data-case=\"{name}\".");

        Strict(shown.InnerHtml.Replace("src=\"../../assets/", "src=\"img/", StringComparison.Ordinal), within)
            .MarkupMatches(Strict(expected, within), $"docs/ui/components/{component}/preview.html, data-case {name}");
    }

    [Fact]
    public void every_preview_case_is_a_contract_case()
    {
        List<string> extra = [];

        foreach (string folder in Directory.GetDirectories(ComponentsRoot()).Where(folder => File.Exists(Path.Combine(folder, "preview.html"))))
        {
            string component = Path.GetFileName(folder);
            extra.AddRange(PreviewCases(component)
                .Select(element => element.GetAttribute("data-case")!)
                .Where(name => !File.Exists(Path.Combine(folder, "cases", name + ".json")))
                .Select(name => component + ": " + name));
        }

        Assert.True(extra.Count == 0, "A preview shows only contract cases (ADR 0206 point 6): " + string.Join("; ", extra));
    }

    // "Copied" follows a click, so no case states it: the button says it, the
    // live region announces it, and both go after two seconds.
    [Fact]
    public void copy_says_copied_announces_it_and_reverts()
    {
        ManualTime time = new();
        using BunitContext context = NewContext(time);
        context.JSInterop.SetupVoid("varveWebUi.copy", "0HN7R3K2M9Q4:00000007").SetVoidResult();

        IRenderedComponent<CopyButton> copy = context.Render<CopyButton>(parameters => parameters.Add(button => button.Text, "0HN7R3K2M9Q4:00000007"));
        Assert.Equal("", copy.Find("[aria-live=polite]").TextContent);

        copy.Find("button").Click();

        Assert.Equal("Copied", copy.Find("button").TextContent);
        Assert.Equal("Copied", copy.Find("[aria-live=polite]").TextContent);

        time.Advance(CopyButton.CopiedFor);

        copy.WaitForAssertion(() =>
        {
            Assert.Equal("Copy", copy.Find("button").TextContent);
            Assert.Equal("", copy.Find("[aria-live=polite]").TextContent);
        });
    }

    [Fact]
    public void a_table_with_no_caption_and_no_label_is_refused()
    {
        using BunitContext context = NewContext(TimeProvider.System);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            context.Render<ResourceTable>(parameters => parameters.Add(table => table.ColumnCount, 1)));
        Assert.Contains("every table has a name", refused.Message, StringComparison.Ordinal);
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

    // A space between two inline elements is visible ("Data plane signed
    // in"), so whitespace counts: the comparison preserves it rather than
    // normalising it away. Only the case file's final newline is not markup.
    private static string Strict(string expected, string[] within) =>
        "<" + within[0] + " diff:whitespace=\"preserve\">"
        + string.Concat(within.Skip(1).Select(element => "<" + element + ">"))
        + expected.TrimEnd('\n')
        + string.Concat(within.Reverse().Select(element => "</" + element + ">"));

    // The elements a case renders inside: a div, or a table body for a row.
    private static string[] Within(JsonDocument parameters) =>
        parameters.RootElement.TryGetProperty("within", out JsonElement within) && within.GetString() == "tbody"
            ? ["table", "tbody"]
            : ["div"];

    private static BunitContext NewContext(TimeProvider time)
    {
        BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(time);
        return context;
    }

    private static IEnumerable<IElement> PreviewCases(string component) =>
        new HtmlParser()
            .ParseDocument(File.ReadAllText(Path.Combine(ComponentsRoot(), component, "preview.html")))
            .QuerySelectorAll("[data-case]");

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

    // A clock that moves only when told: one timer at a time, as Task.Delay
    // makes it.
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<Timer> _timers = [];
        private TimeSpan _now;

        public void Advance(TimeSpan by)
        {
            _now += by;

            foreach (Timer timer in _timers.Where(timer => timer.Due <= _now).ToList())
            {
                _timers.Remove(timer);
                timer.Fire();
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timer timer = new(this, callback, state, _now + dueTime);
            _timers.Add(timer);
            return timer;
        }

        private sealed class Timer(ManualTime time, TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            public TimeSpan Due { get; private set; } = due;

            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = time._now + dueTime;
                return true;
            }

            public void Dispose() => time._timers.Remove(this);

            public System.Threading.Tasks.ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }
        }
    }
}
