// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Turtle;

namespace Varve.Protocol;

/// <summary>
/// Reads <c>application/vnd.varve.delta; version=1</c>, the change feed's and
/// the diff's format (<c>change-feed.md</c> §2), into <see cref="FeedRecord"/>s
/// with Varve's own terms, so that a client consumes a feed with no parser of
/// its own (ADR 0097).
/// </summary>
/// <remarks>
/// <para>
/// A pull reader over input that arrives in pieces: <see cref="TryRead"/>
/// takes what has arrived, consumes one whole record if there is one, and
/// leaves the rest. The answer does not depend on where the input was split
/// (the chunk-boundary rule, <c>testing.md</c> §2). For a server-sent-events
/// stream, each event's <c>data:</c> lines are one record's lines.
/// </para>
/// <para>
/// One reader reads one stream: it counts bytes and lines across calls, so an
/// error names the line it is on.
/// </para>
/// </remarks>
public sealed class ChangeFeedReader
{
    private long _offset;
    private long _line = 1;

    /// <summary>
    /// Reads one record from the front of <paramref name="buffer"/>, consuming
    /// it and any comment or blank line before it. False when no whole record
    /// has arrived yet; with <paramref name="isFinalBlock"/>, false means the
    /// stream ended cleanly between records.
    /// </summary>
    /// <exception cref="ChangeFeedFormatException">The input is not the format, or ends inside a record.</exception>
    public bool TryRead(ref ReadOnlySequence<byte> buffer, bool isFinalBlock, [NotNullWhen(true)] out FeedRecord? record)
    {
        record = null;
        SequenceReader<byte> reader = new(buffer);
        List<ReadOnlySequence<byte>> lines = [];
        long offset = _offset;
        long line = _line;
        long start = -1;
        long startLine = 0;

        while (true)
        {
            if (!reader.TryReadTo(out ReadOnlySequence<byte> text, (byte)'\n'))
            {
                if (isFinalBlock && (lines.Count > 0 || !reader.End))
                {
                    throw new ChangeFeedFormatException("The stream ends inside a record.", start < 0 ? offset : start, start < 0 ? line : startLine);
                }

                return false;
            }

            long lineStart = offset;
            offset += text.Length + 1;
            line++;

            if (text.IsEmpty)
            {
                if (lines.Count == 0)
                {
                    continue; // A blank line between records.
                }

                break;
            }

            if (text.FirstSpan[0] == (byte)'#')
            {
                if (lines.Count > 0)
                {
                    throw new ChangeFeedFormatException("A comment line inside a record.", lineStart, line - 1);
                }

                continue;
            }

            if (lines.Count == 0)
            {
                start = lineStart;
                startLine = line - 1;
            }

            lines.Add(text);
        }

        record = Parse(lines, start, startLine);
        buffer = buffer.Slice(reader.Position);
        _offset = offset;
        _line = line;
        return true;
    }

    /// <summary>Reads every record of a stream until it ends.</summary>
    /// <exception cref="ChangeFeedFormatException">The input is not the format, or ends inside a record.</exception>
    public static async IAsyncEnumerable<FeedRecord> ReadAllAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PipeReader pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        ChangeFeedReader reader = new();

        try
        {
            while (true)
            {
                ReadResult read = await pipe.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = read.Buffer;
                List<FeedRecord> records = [];

                while (reader.TryRead(ref buffer, read.IsCompleted, out FeedRecord? record))
                {
                    records.Add(record);
                }

                pipe.AdvanceTo(buffer.Start, buffer.End);

                foreach (FeedRecord record in records)
                {
                    yield return record;
                }

                if (read.IsCompleted)
                {
                    yield break;
                }
            }
        }
        finally
        {
            await pipe.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static FeedRecord Parse(List<ReadOnlySequence<byte>> lines, long offset, long line)
    {
        string header = Utf8(lines[0], offset, line);
        string[] fields = header.Split(' ');

        switch (fields[0])
        {
            case "commit" when fields.Length == 4:
                return Commit(lines, fields, offset, line);
            case "diff" when fields.Length == 3:
                return FeedRecord.ForDiff(ReadPosition(fields[1], offset, line), ReadPosition(fields[2], offset, line), Changes(lines, 1, offset, line));
            case "error" when fields.Length == 2 && lines.Count == 1 && ProblemType.TryParse(fields[1], out ProblemType problem):
                return FeedRecord.ForError(problem);
            default:
                throw new ChangeFeedFormatException("A record starts with 'commit <position> <kind> <timestamp>', 'diff <from> <to>' or 'error <problem type>'.", offset, line);
        }
    }

    private static FeedRecord Commit(List<ReadOnlySequence<byte>> lines, string[] header, long offset, long line)
    {
        Position position = ReadPosition(header[1], offset, line);
        CommitKind kind = header[2] switch
        {
            "Data" => CommitKind.Data,
            "Settings" => CommitKind.Settings,
            "Erasure" => CommitKind.Erasure,
            _ => throw new ChangeFeedFormatException("A commit's kind is Data, Settings or Erasure.", offset, line),
        };

        if (header[3].Length != 28 || !Instants.TryParse(header[3], out CommitTimestamp timestamp))
        {
            throw new ChangeFeedFormatException("A commit's timestamp is RFC 3339 in UTC with seven fractional digits and Z.", offset, line);
        }

        RdfTerm? agent = null;
        RdfTerm? cause = null;
        RdfTerm? scope = null;
        List<RdfTerm> attachments = [];
        int index = 1;
        int order = 0;

        for (; index < lines.Count; index++)
        {
            ReadOnlySequence<byte> text = lines[index];
            byte first = text.FirstSpan[0];

            if (first is (byte)'+' or (byte)'-')
            {
                break;
            }

            string keyword = Keyword(text, out ReadOnlySequence<byte> rest);
            int rank = keyword switch
            {
                "agent" => 1,
                "cause" => 2,
                "scope" => 3,
                "attachment" => 4,
                _ => throw new ChangeFeedFormatException("A metadata line is agent, cause, scope or attachment.", offset, line + index),
            };

            if (rank < order || (rank == order && rank != 4))
            {
                throw new ChangeFeedFormatException("Metadata lines are agent, cause, scope, then attachments, each at most once.", offset, line + index);
            }

            order = rank;
            RdfTerm term = Term(rest, offset, line + index);

            switch (rank)
            {
                case 1:
                    agent = term;
                    break;
                case 2:
                    cause = term;
                    break;
                case 3:
                    scope = term;
                    break;
                default:
                    attachments.Add(term);
                    break;
            }
        }

        List<FeedChange> changes = Changes(lines, index, offset, line);

        if (kind != CommitKind.Data && changes.Count > 0)
        {
            throw new ChangeFeedFormatException("A Settings or Erasure commit has no changes (I4).", offset, line);
        }

        return FeedRecord.ForCommit(position, kind, timestamp, agent, cause, scope, attachments, changes);
    }

    private static List<FeedChange> Changes(List<ReadOnlySequence<byte>> lines, int from, long offset, long line)
    {
        List<FeedChange> changes = [];
        bool retracting = false;

        for (int i = from; i < lines.Count; i++)
        {
            ReadOnlySequence<byte> text = lines[i];
            byte sign = text.FirstSpan[0];

            if (sign is not ((byte)'+' or (byte)'-') || text.Length < 3)
            {
                throw new ChangeFeedFormatException("A change line is '+ ' or '- ' and a quad.", offset, line + i);
            }

            ReadOnlySequence<byte> separator = text.Slice(1, 1);

            if (separator.FirstSpan[0] != (byte)' ')
            {
                throw new ChangeFeedFormatException("A change line is '+ ' or '- ' and a quad.", offset, line + i);
            }

            bool retract = sign == (byte)'-';

            if (retracting && !retract)
            {
                throw new ChangeFeedFormatException("Assertions come before retractions.", offset, line + i);
            }

            retracting = retract;
            changes.Add(Quad(text.Slice(2), retract ? FeedChangeKind.Retract : FeedChangeKind.Assert, offset, line + i));
        }

        return changes;
    }

    private static FeedChange Quad(ReadOnlySequence<byte> text, FeedChangeKind kind, long offset, long line)
    {
        byte[] statement = new byte[text.Length + 3];
        text.CopyTo(statement);
        " .\n"u8.CopyTo(statement.AsSpan((int)text.Length));
        FeedChange? change = null;
        int count = 0;
        ParseResult result = NQuadsParser.Parse(statement, (in QuadView quad) =>
        {
            count++;
            change = new FeedChange(kind, quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise(),
                quad.HasGraph ? quad.Graph.Materialise() : null);
        }, new ParseOptions { Syntax = RdfSyntax.NQuads });

        if (!result.Succeeded || count != 1 || change is null)
        {
            throw new ChangeFeedFormatException("A change's quad is not one N-Quads statement.", offset, line);
        }

        return change.Value;
    }

    /// <summary>One term in N-Triples syntax, as a change line or a feed filter writes it.</summary>
    internal static bool TryParseTerm(ReadOnlySpan<byte> text, [NotNullWhen(true)] out RdfTerm? term)
    {
        try
        {
            term = Term(new ReadOnlySequence<byte>(text.ToArray()), 0, 0);
            return true;
        }
        catch (ChangeFeedFormatException)
        {
            term = null;
            return false;
        }
    }

    private static RdfTerm Term(ReadOnlySequence<byte> text, long offset, long line)
    {
        ReadOnlySpan<byte> subject = "<urn:varve:s> <urn:varve:p> "u8;
        byte[] statement = new byte[subject.Length + text.Length + 3];
        subject.CopyTo(statement);
        text.CopyTo(statement.AsSpan(subject.Length));
        " .\n"u8.CopyTo(statement.AsSpan(subject.Length + (int)text.Length));
        RdfTerm? term = null;
        int count = 0;
        ParseResult result = NQuadsParser.Parse(statement, (in QuadView quad) =>
        {
            count++;
            term = quad.HasGraph ? null : quad.Object.Materialise();
        }, new ParseOptions { Syntax = RdfSyntax.NQuads });

        if (!result.Succeeded || count != 1 || term is null)
        {
            throw new ChangeFeedFormatException("A metadata line's value is one N-Triples term.", offset, line);
        }

        return term;
    }

    private static string Keyword(ReadOnlySequence<byte> text, out ReadOnlySequence<byte> rest)
    {
        SequenceReader<byte> reader = new(text);

        if (!reader.TryReadTo(out ReadOnlySequence<byte> keyword, (byte)' '))
        {
            rest = default;
            return string.Empty;
        }

        rest = text.Slice(reader.Position);
        return Encoding.UTF8.GetString(keyword);
    }

    private static Position ReadPosition(string text, long offset, long line)
    {
        if (!Instants.IsDecimal(text) || (text.Length > 1 && text[0] == '0')
            || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
        {
            throw new ChangeFeedFormatException("A position is a decimal number with no sign and no leading zero.", offset, line);
        }

        return new Position(value);
    }

    private static string Utf8(ReadOnlySequence<byte> text, long offset, long line)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(text);
        }
        catch (DecoderFallbackException)
        {
            throw new ChangeFeedFormatException("A line is not UTF-8.", offset, line);
        }
    }
}

/// <summary>Input that is not <c>application/vnd.varve.delta; version=1</c>.</summary>
public sealed class ChangeFeedFormatException : Exception
{
    /// <summary>An error with no position.</summary>
    public ChangeFeedFormatException()
    {
    }

    /// <summary>An error with no position.</summary>
    public ChangeFeedFormatException(string message)
        : base(message)
    {
    }

    /// <summary>An error with no position, and its cause.</summary>
    public ChangeFeedFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>An error at the record that starts at <paramref name="offset"/>, on <paramref name="line"/>.</summary>
    public ChangeFeedFormatException(string message, long offset, long line)
        : base(message + " (record at byte " + offset.ToString(CultureInfo.InvariantCulture) + ", line " + line.ToString(CultureInfo.InvariantCulture) + ")")
    {
        Offset = new ByteOffset(offset);
        Line = line;
    }

    /// <summary>The byte offset of the record, from the start of the stream.</summary>
    public ByteOffset Offset { get; }

    /// <summary>The line, from 1, the error is on.</summary>
    public long Line { get; }
}
