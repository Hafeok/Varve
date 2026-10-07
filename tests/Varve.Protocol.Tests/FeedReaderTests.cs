// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Varve.Protocol.Model;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// <c>ChangeFeedReader</c>: the chunk-boundary oracle (<c>testing.md</c> §2) —
/// a document read whole equals the same document split at every offset — and
/// what the format refuses (<c>change-feed.md</c> §2.4).
/// </summary>
public class FeedReaderTests
{
    private const string Document =
        "# a heartbeat between records\n"
        + "commit 1 Data 2026-10-07T12:00:00.0000000Z\n"
        + "agent <https://issuer.example/#alice>\n"
        + "cause \"0HN:1\"\n"
        + "+ <http://ex/s> <http://ex/p> \"o é\"@en <http://ex/g>\n"
        + "+ _:b1 <http://ex/p> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer>\n"
        + "- <http://ex/s> <http://ex/p> <http://ex/o>\n"
        + "\n"
        + "commit 2 Settings 2026-10-07T12:00:01.0000000Z\n"
        + "agent <http://ex/admin>\n"
        + "\n"
        + "#\n"
        + "diff 0 2\n"
        + "+ _:b1 <http://ex/p> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer>\n"
        + "\n"
        + "error https://w3id.org/varve/problems/read-limit-exceeded\n"
        + "\n";

    [Fact]
    public void a_document_reads_the_same_whole_and_split_at_every_offset()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Document);
        List<string> whole = Read([bytes]);
        Assert.Equal(4, whole.Count);

        for (int split = 0; split <= bytes.Length; split++)
        {
            Assert.Equal(whole, Read([bytes[..split], bytes[split..]]));
        }

        for (int a = 0; a <= bytes.Length; a += 7)
        {
            for (int b = a; b <= bytes.Length; b += 11)
            {
                Assert.Equal(whole, Read([bytes[..a], bytes[a..b], bytes[b..]]));
            }
        }
    }

    [Fact]
    public void the_records_carry_what_the_lines_say()
    {
        List<FeedRecord> records = Records([Encoding.UTF8.GetBytes(Document)]);
        FeedRecord first = records[0];
        Assert.Equal(FeedRecordKind.Commit, first.Kind);
        Assert.Equal(1, first.Position.Value);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero), first.Timestamp.Value);
        Assert.Equal("<https://issuer.example/#alice>", P.TermText(first.Agent!));
        Assert.Equal([FeedChangeKind.Assert, FeedChangeKind.Assert, FeedChangeKind.Retract], first.Changes.Select(c => c.Kind));
        Assert.Equal("_:b1", P.TermText(first.Changes[1].Subject));
        Assert.Equal("<http://ex/g>", P.TermText(first.Changes[0].Graph!));
        Assert.Null(first.Changes[2].Graph);

        Assert.Equal(Store.Log.CommitKind.Settings, records[1].CommitKind);
        Assert.Equal((0L, 2L), (records[2].From.Value, records[2].Position.Value));
        Assert.Equal(ProblemType.ReadLimitExceeded, records[3].Problem);
    }

    [Theory]
    [InlineData("commit 1 Data 2026-10-07T12:00:00.0000000Z\n+ <http://ex/s> <http://ex/p>\n\n", "quad", 2)]
    [InlineData("commit 1 Data 2026-10-07T12:00:00Z\n\n", "timestamp", 1)]
    [InlineData("commit 01 Data 2026-10-07T12:00:00.0000000Z\n\n", "position", 1)]
    [InlineData("commit 1 Other 2026-10-07T12:00:00.0000000Z\n\n", "kind", 1)]
    [InlineData("commit 1 Data 2026-10-07T12:00:00.0000000Z\n- <http://ex/s> <http://ex/p> <http://ex/o>\n+ <http://ex/s> <http://ex/p> <http://ex/o>\n\n", "before retractions", 3)]
    [InlineData("commit 1 Data 2026-10-07T12:00:00.0000000Z\ncause \"x\"\nagent <http://ex/a>\n\n", "agent, cause", 3)]
    [InlineData("commit 1 Settings 2026-10-07T12:00:00.0000000Z\n+ <http://ex/s> <http://ex/p> <http://ex/o>\n\n", "I4", 1)]
    [InlineData("commit 1 Data 2026-10-07T12:00:00.0000000Z\n+ <http://ex/s> <http://ex/p> <http://ex/o>\n", "ends inside", 1)]
    [InlineData("hello\n\n", "starts with", 1)]
    public void what_is_not_the_format_is_refused_with_where(string text, string says, long line)
    {
        ChangeFeedFormatException error = Assert.Throws<ChangeFeedFormatException>(() => Records([Encoding.UTF8.GetBytes(text)]));
        Assert.Contains(says, error.Message, StringComparison.Ordinal);
        Assert.Equal(line, error.Line);
    }

    private static List<string> Read(byte[][] chunks) =>
        [.. Records(chunks).Select(r => r.Kind + " " + r.Position.Value + " " + r.CommitKind + " " + r.Timestamp + " " + (r.Agent is null ? "" : P.TermText(r.Agent))
            + " " + string.Join(" | ", r.Changes.Select(c => c.Kind + " " + P.Line(c))) + " " + r.Problem)];

    // Feeds the chunks in order, as a pipe would: what is left unread stays
    // in front of the next chunk.
    private static List<FeedRecord> Records(byte[][] chunks)
    {
        ChangeFeedReader reader = new();
        List<FeedRecord> records = [];
        byte[] pending = [];

        for (int i = 0; i < chunks.Length; i++)
        {
            byte[] joined = [.. pending, .. chunks[i]];
            ReadOnlySequence<byte> buffer = new(joined);

            while (reader.TryRead(ref buffer, i == chunks.Length - 1, out FeedRecord? record))
            {
                records.Add(record);
            }

            pending = buffer.ToArray();
        }

        return records;
    }
}
