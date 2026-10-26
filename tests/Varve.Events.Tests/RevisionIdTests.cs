// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Varve.Delta.Model;
using Varve.Events.Model;
using Xunit;

namespace Varve.Events.Tests;

/// <summary>A schema revision's identifier in its two spaces (ADR 0145, amended by ADR 0167).</summary>
public sealed class RevisionIdTests
{
    [Theory]
    [InlineData("0", 0, false)]
    [InlineData("7", 7, false)]
    [InlineData("m7", 7, true)]
    [InlineData("m0", 0, true)]
    public void a_segment_reads_and_writes_back_unchanged(string text, long position, bool management)
    {
        Assert.True(SchemaRevisionId.TryParse(text, out SchemaRevisionId revision));
        Assert.Equal(position, revision.Position);
        Assert.Equal(management, revision.IsManagement);
        Assert.Equal(text, revision.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("m")]
    [InlineData("07")]
    [InlineData("m07")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("M7")]
    [InlineData(" 7")]
    [InlineData("r7")]
    public void anything_else_is_no_revision(string text) => Assert.False(SchemaRevisionId.TryParse(text, out _));

    [Fact]
    public void every_dataset_revision_sorts_before_every_management_revision()
    {
        SchemaRevisionId late = SchemaRevisionId.Dataset(new FeedPosition(1_000_000));
        SchemaRevisionId early = SchemaRevisionId.Management(1);
        Assert.True(late < early);
        Assert.True(SchemaRevisionId.Dataset(new FeedPosition(2)) < SchemaRevisionId.Dataset(new FeedPosition(3)));
        Assert.True(SchemaRevisionId.Management(2) < SchemaRevisionId.Management(3));
        Assert.NotEqual(SchemaRevisionId.Dataset(new FeedPosition(3)), SchemaRevisionId.Management(3));
    }

    [Fact]
    public void a_history_continued_from_a_move_keeps_an_unchanged_type_s_revision()
    {
        List<FieldSpec> fields = [new("a", "http://www.w3.org/2001/XMLSchema#string", Required: true)];
        List<Q> v1 = new TypeSpec("T", Terms.Ex + "C", 1, fields).Quads(Terms.EventsGraph);
        List<Q> v2 = new TypeSpec("T", Terms.Ex + "C", 2, fields).Quads(Terms.EventsGraph);
        History history = new();
        history.Commit([], v1);
        EventCatalogueHistory dataset = history.Catalogue(history.Head);
        SchemaRevision before = Assert.Single(Assert.Single(dataset.Current.Types).Revisions);
        Assert.Equal(SchemaRevisionId.Dataset(new FeedPosition(1)), before.Revision);

        EventCatalogueHistory moved = EventCatalogueHistory.ContinueFrom(dataset.Current);
        Assert.True(moved.IsManagement);
        moved.Apply(new FeedPosition(5), [.. v1.Select(q => new FeedChange(FeedChangeKind.Assert, q.S, q.P, q.O, q.G))]);
        Assert.Equal(before, Assert.Single(Assert.Single(moved.At(new FeedPosition(5)).Types).Revisions));

        moved.Apply(new FeedPosition(8), [
            .. v1.Except(v2).Select(q => new FeedChange(FeedChangeKind.Retract, q.S, q.P, q.O, q.G)),
            .. v2.Except(v1).Select(q => new FeedChange(FeedChangeKind.Assert, q.S, q.P, q.O, q.G))]);
        CataloguedType after = Assert.Single(moved.Current.Types);
        Assert.Equal([before, new SchemaRevision(2, SchemaRevisionId.Management(8))], after.Revisions);
        Assert.Equal(before, Assert.Single(Assert.Single(moved.At(new FeedPosition(7)).Types).Revisions));
    }
}
