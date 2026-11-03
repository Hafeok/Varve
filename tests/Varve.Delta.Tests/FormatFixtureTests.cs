// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Varve.Delta.Model;
using Varve.Rdf;
using Xunit;

namespace Varve.Delta.Tests;

/// <summary>
/// Delta format 1 as v0.1.0-preview.6 wrote it, from that release's published
/// package (testdata/formats/delta-preview.6, ADR 0216): current code reads it
/// record by record as the release's reader did, and writes it back byte for
/// byte.
/// </summary>
public sealed class FormatFixtureTests
{
    [Fact]
    public void version_one_as_its_release_wrote_it_reads_record_by_record_and_writes_back_byte_for_byte()
    {
        string fixture = Fixture("delta-preview.6");
        byte[] document = File.ReadAllBytes(Path.Combine(fixture, "version-1.delta"));
        ChangeFeedReader reader = new();
        ReadOnlySequence<byte> buffer = new(document);
        List<FeedRecord> records = [];

        while (reader.TryRead(ref buffer, true, out FeedRecord? record))
        {
            records.Add(record);
        }

        Assert.Equal(File.ReadAllText(Path.Combine(fixture, "expectations", "records.txt")), Describe(records));
        ArrayBufferWriter<byte> output = new();

        foreach (FeedRecord record in records)
        {
            DeltaWriter.WriteRecord(output, record);
        }

        Assert.Equal(document, output.WrittenSpan.ToArray());
    }

    // As the fixture's generator describes the records its release read.
    private static string Describe(List<FeedRecord> records)
    {
        StringBuilder expected = new();

        foreach (FeedRecord record in records)
        {
            expected.Append(record.Kind).Append(' ').Append(record.Position).Append(' ').Append(record.From)
                .Append(' ').Append(record.CommitKind).Append(' ').Append(record.Timestamp)
                .Append(" agent=").Append(Term(record.Agent)).Append(" cause=").Append(Term(record.Cause)).Append(" scope=").Append(Term(record.Scope))
                .Append(" problem=").Append(record.Problem).Append('\n');

            foreach (RdfTerm attachment in record.Attachments)
            {
                expected.Append("  attachment ").Append(Term(attachment)).Append('\n');
            }

            foreach (FeedChange change in record.Changes)
            {
                expected.Append("  ").Append(change.Kind).Append(' ').Append(Term(change.Subject)).Append(' ').Append(Term(change.Predicate))
                    .Append(' ').Append(Term(change.Object)).Append(' ').Append(Term(change.Graph)).Append('\n');
            }
        }

        return expected.ToString();
    }

    private static string Term(RdfTerm? term) =>
        term is null ? "none" : term.Kind + "|" + Text(term.Lexical) + "|" + Text(term.DatatypeIri) + "|" + Text(term.Language);

    private static string Text(ReadOnlySpan<byte> utf8)
    {
        StringBuilder text = new();

        foreach (Rune rune in Encoding.UTF8.GetString(utf8).EnumerateRunes())
        {
            _ = rune.Value is >= 0x20 and < 0x7f and not '\\' and not '|' ? text.Append((char)rune.Value) : text.Append("\\u{").Append(rune.Value.ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append('}');
        }

        return text.ToString();
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
