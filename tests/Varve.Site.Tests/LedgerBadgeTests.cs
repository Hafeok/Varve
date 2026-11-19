// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Varve.Site.Tests;

/// <summary>
/// An ADR's badge is read from the ledger, never from its status line (ADR
/// 0247 amended 2026-11-19; the maintainer's ruling 4 on #368, as corrected).
/// One fixture ADR per case, in <c>tests/fixtures/site/ledger/</c>, because
/// <c>main</c> has no "Not yet accepted" case of its own.
/// </summary>
public class LedgerBadgeTests
{
    private static readonly Ledger Fixture = Ledger.Read(new Repository(Paths.Fixture("ledger")));

    [Fact]
    public void every_ruling_accepted_is_accepted()
    {
        AdrState state = Fixture.StateOf("0001");
        Assert.Equal(new Badge(Tone.Ok, "Accepted"), state.Badge);
        Assert.Equal("accepted", state.Set);
        Assert.Empty(state.Notes);
    }

    [Fact]
    public void some_rulings_accepted_and_some_not_is_accepted_with_the_count_pending()
    {
        AdrState state = Fixture.StateOf("0002");
        Assert.Equal(new Badge(Tone.Warn, "Accepted · 1 pending"), state.Badge);
        Assert.Equal([new Pending(null, 1)], state.Pending);
    }

    [Fact]
    public void no_ruling_accepted_is_not_yet_accepted_without_a_count()
    {
        AdrState state = Fixture.StateOf("0006");
        Assert.Equal(new Badge(Tone.Warn, "Not yet accepted"), state.Badge);
        Assert.Equal([new Pending(null, 1)], state.Pending);
    }

    [Fact]
    public void pending_rulings_are_grouped_by_the_amendment_their_statements_date()
    {
        AdrState state = Fixture.StateOf("0010");
        Assert.Equal(new Badge(Tone.Warn, "Accepted · 3 pending"), state.Badge);
        Assert.Equal([new Pending("2026-11-19", 2), new Pending(null, 1)], state.Pending);
    }

    [Fact]
    public void the_head_says_which_amendment_the_pending_rulings_belong_to_and_links_its_section()
    {
        Head head = SiteBuild.AdrHead("0010", "Accepted, with a pending amendment", Fixture, "adr/0010-x.html", ["context", "amendment-2026-11-19--two-rulings-added"]);
        Assert.Equal("ADR 0010", head.Eyebrow);
        List<(string Term, string Value)> meta = [.. head.Meta!];
        Assert.Equal(("Status", "<span class=\"vw-state vw-state--warn\">Accepted &#183; 3 pending</span>"), meta[0]);
        Assert.Equal(("Pending", "2 rulings, from <a href=\"#amendment-2026-11-19--two-rulings-added\">the amendment of 2026-11-19</a>"), meta[2]);
        Assert.Equal(("Pending", "1 ruling, not dated by an amendment"), meta[3]);
        Assert.Equal(4, meta.Count);
    }

    [Fact]
    public void an_accepted_adr_names_nothing_pending()
    {
        Head head = SiteBuild.AdrHead("0001", "Every ruling accepted", Fixture, "adr/0001-x.html", []);
        Assert.Equal(["Status", "Decision set"], head.Meta!.Select(static m => m.Term));
    }

    [Fact]
    public void an_adr_the_index_supersedes_is_superseded_whatever_its_status_line_says()
    {
        // The case of ADR 0002 on main: no set, a status line that says
        // Accepted, and an index row that says it is superseded.
        AdrState state = Fixture.StateOf("0003");
        Assert.Equal(new Badge(Tone.Neutral, "Superseded"), state.Badge);
        Assert.Null(state.Set);
    }

    [Fact]
    public void a_later_clause_of_the_index_cell_that_supersedes_it_is_read()
    {
        // The case of ADR 0045: "Accepted, provisional by design; superseded by 0072".
        Assert.Equal(new Badge(Tone.Neutral, "Superseded"), Fixture.StateOf("0004").Badge);
    }

    [Fact]
    public void an_adr_superseded_in_part_keeps_the_badge_of_its_set()
    {
        Assert.Equal(new Badge(Tone.Ok, "Accepted"), Fixture.StateOf("0009").Badge);
    }

    [Fact]
    public void an_accepted_adr_with_an_unaccepted_amendment_keeps_its_badge_and_carries_a_dated_note()
    {
        AdrState amended = Fixture.StateOf("0005");
        Assert.Equal(new Badge(Tone.Ok, "Accepted"), amended.Badge);
        AmendmentNote note = Assert.Single(amended.Notes);
        Assert.Equal(new AmendmentNote("0006", "2026-11-19", 1), note);

        // The amending ADR's own badge is its own set's.
        Assert.Equal(new Badge(Tone.Warn, "Not yet accepted"), Fixture.StateOf("0006").Badge);
        Assert.Empty(Fixture.StateOf("0006").Notes);
    }

    [Fact]
    public void a_revoked_ruling_is_out_of_force_and_counts_for_neither()
    {
        Assert.Equal(new Badge(Tone.Ok, "Accepted"), Fixture.StateOf("0007").Badge);
    }

    [Fact]
    public void an_adr_with_no_set_that_is_not_superseded_shows_its_status_lines_first_word_in_a_neutral_tone()
    {
        AdrState state = Fixture.StateOf("0008");
        Assert.Equal(new Badge(Tone.Neutral, "Proposed"), state.Badge);
        Assert.Null(state.Set);
    }

    [Fact]
    public void the_badge_is_drawn_with_the_design_systems_state_atom()
    {
        Assert.Equal("<span class=\"vw-state vw-state--warn\">Accepted &#183; 2 pending</span>", new Badge(Tone.Warn, "Accepted · 2 pending").Html);
        Assert.Equal("<span class=\"vw-state vw-state--neutral\">Superseded</span>", new Badge(Tone.Neutral, "Superseded").Html);
    }

    [Theory]
    [InlineData("**Superseded by 0031**", true)]
    [InlineData("Accepted, provisional by design; **superseded by 0072**", true)]
    [InlineData("**Superseded by 0088**; amended 2026-09-24", true)]
    [InlineData("Accepted; layer table **superseded by 0060**; declaration **by 0064**", false)]
    [InlineData("Accepted; \"No temporal index\" **superseded in part by 0138** (filed unaccepted)", false)]
    [InlineData("Accepted; **amended by 0068**", false)]
    public void the_index_says_superseded_only_of_the_whole_adr(string cell, bool superseded) =>
        Assert.Equal(superseded, Ledger.IsWhollySuperseded(cell));

    [Fact]
    public void on_main_every_adr_has_a_badge_and_none_is_read_from_a_status_line()
    {
        // Every ADR on main either has a set or is superseded in the index:
        // the fallback to a status line's first word is for none of them.
        Repository repository = new(Paths.Root);
        Ledger ledger = Ledger.Read(repository);

        foreach (string file in Directory.EnumerateFiles(repository.Full("docs/adr"), "0*.md"))
        {
            string number = Ledger.NumberOf(Path.GetFileName(file))!;
            AdrState state = ledger.StateOf(number);
            Assert.True(state.Set is not null || state.Badge.Word == "Superseded", $"ADR {number} has no set and is not superseded in the index.");
        }

        Assert.Equal(new Badge(Tone.Neutral, "Superseded"), ledger.StateOf("0002").Badge);
    }
}
