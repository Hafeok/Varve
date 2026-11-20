// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using Varve.Delta.Model;
using Varve.Protocol.Client.Model;
using Varve.Rdf;
using Varve.WebUi.Explorer.Parts;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// The explorer's three domain controls, as parts over generic components
/// (ADR 0206, ruling D8): the generic components' cases pin the markup, and
/// these tests pin what each part decides.
/// </summary>
public class ExplorerPartsTests
{
    private const string Regimes = "http://www.w3.org/ns/entailment/";

    [Fact]
    public void the_regime_select_offers_simple_alone_until_the_description_is_read()
    {
        using BunitContext context = new();
        IRenderedComponent<RegimeSelect> select = context.Render<RegimeSelect>(p => p.Add(s => s.Id, "regime"));

        Assert.Equal(["simple"], Values(select));
        Assert.Equal("The dataset's default is simple.", select.Find(".vw-field__help").TextContent);
    }

    [Fact]
    public void the_regime_select_offers_the_regimes_the_description_lists_and_raises_the_one_chosen()
    {
        using BunitContext context = new();
        Entailment? chosen = null;
        IRenderedComponent<RegimeSelect> select = context.Render<RegimeSelect>(p => p
            .Add(s => s.Id, "regime")
            .Add(s => s.Description, Description(Regimes + "Simple", Regimes + "Simple", Regimes + "RDFS"))
            .Add(s => s.SelectedChanged, (Entailment regime) => chosen = regime));

        Assert.Equal(["simple", "rdfs"], Values(select));
        select.Find("select").Change("rdfs");
        Assert.Equal(new Entailment("rdfs"), chosen);
    }

    [Fact]
    public void a_regime_the_client_knows_no_token_of_is_not_offered_and_the_default_is_selected()
    {
        using BunitContext context = new();
        IRenderedComponent<RegimeSelect> select = context.Render<RegimeSelect>(p => p
            .Add(s => s.Id, "regime")
            .Add(s => s.Description, Description(Regimes + "RDFS", "http://example.org/regime/OWL-Direct", Regimes + "RDFS")));

        Assert.Equal(["simple", "rdfs"], Values(select));
        Assert.True(select.Find("option[value=rdfs]").HasAttribute("selected"));
        Assert.Equal("The dataset's default is rdfs.", select.Find(".vw-field__help").TextContent);
    }

    [Fact]
    public void the_as_of_control_reads_at_the_head_with_no_field()
    {
        using BunitContext context = new();
        IRenderedComponent<AsOfControl> control = context.Render<AsOfControl>(p => p.Add(c => c.Id, "asof"));

        Assert.True(control.Find("option[value=head]").HasAttribute("selected"));
        Assert.Empty(control.FindAll("input"));
    }

    [Fact]
    public void a_position_handed_in_is_shown_and_its_resolution_said()
    {
        using BunitContext context = new();
        IRenderedComponent<AsOfControl> control = context.Render<AsOfControl>(p => p
            .Add(c => c.Id, "asof")
            .Add(c => c.Selector, AsOfSelector.At(new FeedPosition(16012)))
            .Add(c => c.Resolved, new FeedPosition(16012))
            .Add(c => c.ResolvedCommitted, new CommitTime(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero))));

        Assert.Equal("16012", control.Find("input").GetAttribute("value"));
        Assert.Equal("→ position 16,012, committed 2026-11-19T10:00:00Z.", control.Find(".vw-field__help").TextContent);
    }

    [Fact]
    public void a_time_is_converted_to_utc_and_the_line_says_so()
    {
        using BunitContext context = new();
        List<AsOfSelector?> raised = [];
        IRenderedComponent<AsOfControl> control = context.Render<AsOfControl>(p => p
            .Add(c => c.Id, "asof")
            .Add(c => c.SelectorChanged, (AsOfSelector? selector) => raised.Add(selector)));

        control.Find("select").Change("time");
        control.Find("input").Input("2026-11-19T11:00:00+01:00");

        Assert.Equal(AsOfSelector.AtTime(new AsOfTime(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero))), raised[^1]);
        Assert.Equal("Read as 2026-11-19T10:00:00Z, UTC: the last commit at or before it.", control.Find(".vw-field__help").TextContent);
    }

    [Fact]
    public void a_position_too_far_below_the_head_is_refused_before_sending_with_the_limit_stated()
    {
        using BunitContext context = new();
        List<AsOfSelector?> raised = [];
        IRenderedComponent<AsOfControl> control = context.Render<AsOfControl>(p => p
            .Add(c => c.Id, "asof")
            .Add(c => c.Head, new FeedPosition(18417))
            .Add(c => c.MaxDistance, new AsOfDistance(10000))
            .Add(c => c.SelectorChanged, (AsOfSelector? selector) => raised.Add(selector)));

        control.Find("select").Change("position");
        control.Find("input").Input("100");

        Assert.Null(raised[^1]);
        Assert.Equal("Position 100 is 18,317 below the head at 18,417. This server reads at most 10,000 below the head.", control.Find(".vw-field__error").TextContent);
        Assert.Equal("true", control.Find("input").GetAttribute("aria-invalid"));

        control.Find("input").Input("8417");
        Assert.Equal(AsOfSelector.At(new FeedPosition(8417)), raised[^1]);
        Assert.Empty(control.FindAll(".vw-field__error"));
    }

    [Theory]
    [InlineData("position", "sixteen", "A position is a whole number, such as 16012.")]
    [InlineData("position", "-3", "A position is a whole number, such as 16012.")]
    [InlineData("time", "yesterday", "A time is an ISO 8601 date and time, such as 2026-11-19T10:00:00Z.")]
    [InlineData("time", "2026-11-19", "A time is an ISO 8601 date and time, such as 2026-11-19T10:00:00Z.")]
    public void what_is_not_a_selector_is_a_field_error_and_raises_none(string mode, string typed, string error)
    {
        using BunitContext context = new();
        List<AsOfSelector?> raised = [];
        IRenderedComponent<AsOfControl> control = context.Render<AsOfControl>(p => p
            .Add(c => c.Id, "asof")
            .Add(c => c.SelectorChanged, (AsOfSelector? selector) => raised.Add(selector)));

        control.Find("select").Change(mode);
        control.Find("input").Input(typed);

        Assert.Null(raised[^1]);
        Assert.Equal(error, control.Find(".vw-field__error").TextContent);
    }

    [Fact]
    public void what_is_typed_survives_a_render_with_the_same_selector_handed_in()
    {
        using BunitContext context = new();
        IRenderedComponent<AsOfControl> control = context.Render<AsOfControl>(p => p.Add(c => c.Id, "asof"));

        control.Find("select").Change("position");
        control.Find("input").Input("12");
        control.Render(p => p.Add(c => c.Resolved, new FeedPosition(12)));

        Assert.Equal("12", control.Find("input").GetAttribute("value"));

        control.Render(p => p.Add(c => c.Selector, AsOfSelector.At(new FeedPosition(40))));
        Assert.Equal("40", control.Find("input").GetAttribute("value"));
    }

    [Theory]
    [InlineData(QueryForm.Solutions, "SPARQL XML|SPARQL JSON|CSV|TSV")]
    [InlineData(QueryForm.Boolean, "SPARQL XML|SPARQL JSON")]
    [InlineData(QueryForm.Graph, "N-Triples|Turtle|N-Quads|TriG|RDF/XML|JSON-LD")]
    public void the_download_menu_offers_the_formats_the_server_writes_for_the_form(QueryForm form, string formats)
    {
        using BunitContext context = new();
        DownloadFormat? chosen = null;
        IRenderedComponent<DownloadMenu> menu = context.Render<DownloadMenu>(p => p
            .Add(m => m.Id, "download")
            .Add(m => m.Form, form)
            .Add(m => m.OnDownload, (DownloadFormat format) => chosen = format));

        menu.Find("button").Click();
        IReadOnlyList<IElement> items = menu.FindAll(".vw-menu__item");

        Assert.Equal(formats.Split('|'), items.Select(item => item.TextContent));
        items[^1].Click();
        Assert.Equal(DownloadFormat.For(form)[^1], chosen);
    }

    [Fact]
    public void a_running_download_says_so_beside_its_format_takes_no_second_choice_and_closes_the_menu_when_it_ends()
    {
        using BunitContext context = new();
        DownloadFormat? chosen = null;
        DownloadFormat csv = DownloadFormat.For(QueryForm.Solutions)[2];
        IRenderedComponent<DownloadMenu> menu = context.Render<DownloadMenu>(p => p
            .Add(m => m.Id, "download")
            .Add(m => m.Busy, true)
            .Add(m => m.Downloading, csv)
            .Add(m => m.Progress, "1.2 MB")
            .Add(m => m.OnDownload, (DownloadFormat format) => chosen = format));

        menu.Find("button").Click();

        Assert.Equal("1.2 MB", menu.Find(".vw-menu__note").TextContent);
        Assert.All(menu.FindAll(".vw-menu__item"), item => Assert.Equal("true", item.GetAttribute("aria-disabled")));
        menu.FindAll(".vw-menu__item")[0].Click();
        Assert.Null(chosen);

        menu.Render(p => p.Add(m => m.Busy, false));
        Assert.Empty(menu.FindAll(".vw-menu__list"));
        Assert.Equal("false", menu.Find("button").GetAttribute("aria-expanded"));
    }

    // #377: a triple term in a results table is its three terms, written
    // out in the triple's frame, never an empty chip.
    [Fact]
    public void a_triple_term_value_is_written_out_in_its_frame()
    {
        using BunitContext context = new();
        RdfTerm triple = RdfTerm.TripleTerm(Iri("http://purl.org/dc/terms/creator"), Iri("http://purl.org/dc/terms/title"), RdfTerm.Literal(Encoding.UTF8.GetBytes("x")));
        Varve.WebUi.Explorer.Atoms.TermValue value = TermValues.Of(triple);
        IRenderedComponent<Varve.WebUi.Components.Atoms.TermChip> chip = context.Render<Varve.WebUi.Components.Atoms.TermChip>(p => p
            .Add(c => c.Kind, value.Kind)
            .Add(c => c.Text, value.Text));

        Assert.Equal("<< dct:creator dct:title x >>", chip.Find(".vw-term").TextContent);
    }

    private static string[] Values(IRenderedComponent<RegimeSelect> select) =>
        [.. select.FindAll("option").Select(option => option.GetAttribute("value")!)];

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    // A description whose one graph is served under the regimes given.
    private static DatasetDescription Description(string fallback, params string[] regimes) =>
        new(
            [],
            Iri(fallback),
            new Dictionary<RdfTerm, IReadOnlyList<RdfTerm>> { [Iri("http://example.org/g")] = [.. regimes.Select(Iri)] },
            null,
            new Dictionary<RdfTerm, RdfTerm>());
}
