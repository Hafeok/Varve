// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CsCheck;
using Varve.Delta.Model;
using Varve.Rdf;
using Xunit;

namespace Varve.Delta.Tests;

/// <summary>
/// The writer and the reader against each other over generated records, and
/// version 1 against a document written once and kept (ADR 0128): what the
/// writer writes the reader reads to the same record, whole or split at any
/// offset, and what was version 1 when it was written is read, and written
/// back, byte for byte by every later build.
/// </summary>
public class RoundTripTests
{
    internal const int Iterations = 1_000;

    private static readonly Gen<string> Name = Gen.String[Gen.Char.AlphaNumeric, 1, 8];

    private static readonly Gen<RdfTerm> Iri = Name.Select(n => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + n)));

    private static readonly Gen<RdfTerm> BlankNode = Name.Select(n => RdfTerm.BlankNode(Encoding.UTF8.GetBytes("b" + n)));

    private static readonly Gen<string> Lexical = Gen.String[
        Gen.OneOf(Gen.Char.AlphaNumeric, Gen.OneOfConst(' ', '"', '\\', '\n', '\r', '\t', 'é', '中')),
        0,
        12];

    private static readonly Gen<RdfTerm> Literal = Gen.OneOf(
        Lexical.Select(l => RdfTerm.Literal(Encoding.UTF8.GetBytes(l))),
        Gen.Select(Lexical, Name, (l, d) => RdfTerm.Literal(Encoding.UTF8.GetBytes(l), RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/dt/" + d)))),
        Gen.Select(Lexical, Gen.OneOfConst("en", "de-DE", "zh-Hans"), (l, lang) => RdfTerm.Literal(Encoding.UTF8.GetBytes(l), Encoding.UTF8.GetBytes(lang), TextDirection.None)));

    private static readonly Gen<FeedChange> Change = Gen.Select(
        Gen.OneOf(Iri, BlankNode),
        Iri,
        Gen.OneOf(Iri, BlankNode, Literal),
        Gen.Frequency((3, Gen.Const((RdfTerm?)null)), (1, Iri.Select(g => (RdfTerm?)g))),
        Gen.Bool,
        (s, p, o, g, assert) => new FeedChange(assert ? FeedChangeKind.Assert : FeedChangeKind.Retract, s, p, o, g));

    private static readonly Gen<List<FeedChange>> Changes =
        Change.List[0, 6].Select(changes => changes.OrderBy(c => c.Kind).ToList());

    private static readonly Gen<FeedTimestamp> Timestamp =
        Gen.Long[0, 3_000_000_000_000_000L].Select(ticks => new FeedTimestamp(new DateTimeOffset(DateTime.UnixEpoch.Ticks + ticks, TimeSpan.Zero)));

    private static readonly Gen<FeedRecord> Commit = Gen.Select(
        Gen.Long[1, long.MaxValue],
        Gen.Frequency((4, Gen.Const(FeedCommitKind.Data)), (1, Gen.Const(FeedCommitKind.Settings)), (1, Gen.Const(FeedCommitKind.Erasure))),
        Timestamp,
        Gen.Select(Gen.OneOf(Gen.Const((RdfTerm?)null), Iri.Select(i => (RdfTerm?)i)), Gen.OneOf(Gen.Const((RdfTerm?)null), Literal.Select(l => (RdfTerm?)l)), Gen.OneOf(Gen.Const((RdfTerm?)null), Iri.Select(i => (RdfTerm?)i))),
        Iri.List[0, 2],
        Changes,
        (position, kind, timestamp, metadata, attachments, changes) => FeedRecord.ForCommit(
            new FeedPosition(position), kind, timestamp, metadata.Item1, metadata.Item2, metadata.Item3, attachments,
            kind == FeedCommitKind.Data ? changes : []));

    private static readonly Gen<FeedRecord> Diff = Gen.Select(
        Gen.Long[0, 1_000_000],
        Gen.Long[0, 1_000_000],
        Changes,
        (from, to, changes) => FeedRecord.ForDiff(new FeedPosition(from), new FeedPosition(to), changes));

    private static readonly Gen<FeedRecord> Error =
        Gen.OneOfConst("read-limit-exceeded", "shutting-down").Select(name => FeedRecord.ForError(FeedProblemType.Parse(FeedProblemType.Namespace + name)));

    private static readonly Gen<List<FeedRecord>> Document = Gen.Frequency((6, Commit), (2, Diff), (1, Error)).List[1, 6];

    [Fact]
    public void every_written_record_reads_back_to_itself_whole_and_split_at_any_offset()
    {
        Gen.Select(Document, Gen.Int[0, 1_000_000]).Sample((records, at) =>
        {
            byte[] bytes = Write(records);
            List<FeedRecord> read = Read([bytes]);
            Assert.Equal(bytes, Write(read));

            int split = at % (bytes.Length + 1);
            Assert.Equal(bytes, Write(Read([bytes[..split], bytes[split..]])));
        }, iter: Iterations);
    }

    [Fact]
    public void version_one_is_read_and_written_back_byte_for_byte()
    {
        byte[] document = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "version-1.delta"));
        List<FeedRecord> records = Read([document]);
        Assert.Equal(7, records.Count);
        Assert.Equal(document, Write(records));
    }

    [Fact]
    public void the_current_version_is_one_and_names_its_media_type()
    {
        Assert.Equal(1, DeltaFormat.Current.Value);
        Assert.Equal("application/vnd.varve.delta; version=1", DeltaFormat.Current.MediaType);
    }

    private static byte[] Write(IEnumerable<FeedRecord> records)
    {
        ArrayBufferWriter<byte> output = new();

        foreach (FeedRecord record in records)
        {
            DeltaWriter.WriteRecord(output, record);
        }

        return output.WrittenSpan.ToArray();
    }

    private static List<FeedRecord> Read(byte[][] chunks)
    {
        ChangeFeedReader reader = new();
        List<FeedRecord> records = [];
        byte[] pending = [];

        for (int i = 0; i < chunks.Length; i++)
        {
            ReadOnlySequence<byte> buffer = new([.. pending, .. chunks[i]]);

            while (reader.TryRead(ref buffer, i == chunks.Length - 1, out FeedRecord? record))
            {
                records.Add(record);
            }

            pending = buffer.ToArray();
        }

        return records;
    }
}
