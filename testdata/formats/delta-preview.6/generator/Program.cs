// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Writes delta format 1 as v0.1.0-preview.6 writes it, from that release's
// published Varve.Delta package (docs/formats/, ADR 0216). The records are
// listed in records.delta, spelled out; this release's reader parses them and
// its writer writes them, one record after another, so every byte of
// version-1.delta is the release's. Beside it, expectations/records.txt: each
// record as the release's reader gives it, field by field.
using System.Buffers;
using System.Text;
using Varve.Delta;
using Varve.Delta.Model;
using Varve.Rdf;

string input = args[0];
string root = args[1];
ArrayBufferWriter<byte> output = new();
StringBuilder expected = new();
List<FeedRecord> records = [];

await using (FileStream stream = File.OpenRead(input))
{
    await foreach (FeedRecord record in ChangeFeedReader.ReadAllAsync(stream))
    {
        records.Add(record);
    }
}

foreach (FeedRecord record in records)
{
    DeltaWriter.WriteRecord(output, record);
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

Directory.CreateDirectory(Path.Combine(root, "expectations"));
File.WriteAllBytes(Path.Combine(root, "version-1.delta"), output.WrittenSpan.ToArray());
File.WriteAllText(Path.Combine(root, "expectations", "records.txt"), expected.ToString());
Console.WriteLine(records.Count + " records, " + output.WrittenCount + " bytes");

// A term, field by field: its kind, its lexical form as UTF-8 escaped to
// ASCII, its datatype IRI and its language; none when absent.
static string Term(RdfTerm? term)
{
    if (term is null)
    {
        return "none";
    }

    string Text(ReadOnlySpan<byte> utf8)
    {
        StringBuilder text = new();

        foreach (Rune rune in Encoding.UTF8.GetString(utf8).EnumerateRunes())
        {
            _ = rune.Value is >= 0x20 and < 0x7f and not '\\' and not '|' ? text.Append((char)rune.Value) : text.Append("\\u{").Append(rune.Value.ToString("x")).Append('}');
        }

        return text.ToString();
    }

    return term.Kind + "|" + Text(term.Lexical) + "|" + Text(term.DatatypeIri) + "|" + Text(term.Language);
}
