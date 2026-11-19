// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using Varve.Delta.Model;
using Varve.Protocol.Client.Model;
using Varve.WebUi.Explorer.Parts;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// What the Query page decides without the server: the answer's form from the
/// first query keyword (ADR 0250), the bracket pair at the caret, and the
/// tab's history (ADR 0249 point 7).
/// </summary>
public class QueryPageLogicTests
{
    [Theory]
    [InlineData("SELECT * WHERE { ?s ?p ?o }", QueryForm.Solutions)]
    [InlineData("prefix ex: <http://ex/> # a comment\nselect ?s { ?s ex:p ?o }", QueryForm.Solutions)]
    [InlineData("ASK { ?s ?p ?o }", QueryForm.Boolean)]
    [InlineData("CONSTRUCT { ?s ?p ?o } WHERE { ?s ?p ?o }", QueryForm.Graph)]
    [InlineData("DESCRIBE <http://ex/s>", QueryForm.Graph)]
    public void the_form_is_the_first_query_keyword(string query, QueryForm form) =>
        Assert.Equal(form, QueryColouring.Form(query));

    [Theory]
    [InlineData("")]
    [InlineData("PREFIX ex: <http://ex/>")]
    [InlineData("# SELECT in a comment")]
    public void before_a_query_keyword_there_is_no_form(string query) =>
        Assert.Null(QueryColouring.Form(query));

    [Theory]
    // The caret after an opening brace, and before its closing one.
    [InlineData("SELECT * { ?s ?p ( 1 2 ) }", 10, 9, 25)]
    [InlineData("SELECT * { ?s ?p ( 1 2 ) }", 25, 9, 25)]
    // Nested: the parenthesis just typed matches its own partner, not the brace.
    [InlineData("SELECT * { ?s ?p ( 1 2 ) }", 24, 17, 23)]
    public void the_bracket_at_the_caret_matches_its_partner(string query, int caret, int open, int close)
    {
        int[] match = [.. QueryColouring.Match(query, QueryColouring.Runs(query), caret)];

        Assert.Equal([open, close], match);
    }

    [Theory]
    [InlineData("SELECT * { ?s ?p ?o", 10)]
    [InlineData("SELECT * { ?s ?p ?o }", 3)]
    public void an_unmatched_bracket_or_none_at_the_caret_matches_nothing(string query, int caret) =>
        Assert.Empty(QueryColouring.Match(query, QueryColouring.Runs(query), caret));

    [Fact]
    public void the_history_keeps_twenty_newest_first_each_once_and_round_trips()
    {
        QueryHistory history = QueryHistory.Read(null);

        for (int i = 0; i < 25; i++)
        {
            history = history.With(new HistoryEntry("SELECT " + i + " {}", "notes", "", "head"));
        }

        history = history.With(new HistoryEntry("SELECT 10 {}", "notes", "", "head"));
        QueryHistory read = QueryHistory.Read(history.ToJson());

        Assert.Equal(QueryHistory.Capacity, read.Entries.Count);
        Assert.Equal("SELECT 10 {}", read.Entries[0].Query);
        Assert.Single(read.Entries, entry => entry.Query == "SELECT 10 {}");
        Assert.Equal("SELECT 24 {}", read.Entries[1].Query);
        Assert.Equal(history.Entries, read.Entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"query\":\"SELECT\"}")]
    [InlineData("[{\"query\":\"SELECT\",\"dataset\":\"notes\",\"regime\":\"\",\"asOf\":\"tomorrow\"}]")]
    public void what_storage_holds_that_is_not_a_history_is_dropped(string? stored) =>
        Assert.Empty(QueryHistory.Read(stored).Entries);

    [Fact]
    public void a_selector_is_written_in_the_headers_own_forms_and_read_back()
    {
        AsOfSelector[] selectors =
        [
            AsOfSelector.Head,
            AsOfSelector.At(new FeedPosition(16012)),
            AsOfSelector.AtTime(new AsOfTime(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero))),
        ];

        Assert.Equal(["head", "position:16012", "time:2026-11-19T10:00:00.0000000Z"], selectors.Select(QueryHistory.Write));
        Assert.All(selectors, selector => Assert.Equal(selector, QueryHistory.ReadSelector(QueryHistory.Write(selector))));
    }
}
