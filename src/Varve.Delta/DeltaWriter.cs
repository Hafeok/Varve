// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Delta;

/// <summary>Names a handle with its term: a store's dictionary, or any other source of the handles a delta holds.</summary>
[Contract(typeof(VarveDeltaAndTheReadForeverRule.StoreDoesNotReferenceDelta), Role = "the source of terms for the handles a written delta holds, so the writer needs no store")]
public delegate bool TermNamer(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term);

/// <summary>
/// Writes <c>application/vnd.varve.delta; version=1</c> (<see cref="DeltaFormat"/>,
/// <c>change-feed.md</c> §2): commit, diff and error records, and their
/// server-sent-events framing (§3), the format <see cref="ChangeFeedReader"/>
/// reads. Terms are canonical N-Triples; a blank node keeps the label its
/// <see cref="TermNamer"/> gives it (ADR 0098).
/// </summary>
public static class DeltaWriter
{
    private static readonly WriteOptions Canonical = new() { Syntax = RdfSyntax.NQuads, Canonical = true };

    /// <summary>A commit record with <paramref name="delta"/> as its changes, the empty line that ends it included.</summary>
    public static void WriteCommit(IBufferWriter<byte> output, in FeedCommitHeader header, QuadDelta delta, TermNamer names)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(names);
        WriteHeader(output, header.Position, header.Kind, header.Timestamp);
        WriteMetadata(output, "agent "u8, header.Agent, names);
        WriteMetadata(output, "cause "u8, header.Cause, names);
        WriteMetadata(output, "scope "u8, header.Scope, names);

        foreach (TermHandle attachment in header.Attachments.Span)
        {
            WriteMetadata(output, "attachment "u8, attachment, names);
        }

        WriteChanges(output, delta, names);
        Write(output, "\n"u8);
    }

    /// <summary>A diff record between two positions, the empty line that ends it included.</summary>
    public static void WriteDiff(IBufferWriter<byte> output, FeedPosition from, FeedPosition to, QuadDelta delta, TermNamer names)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(names);
        Write(output, "diff "u8);
        WriteNumber(output, from.Value);
        Write(output, " "u8);
        WriteNumber(output, to.Value);
        Write(output, "\n"u8);
        WriteChanges(output, delta, names);
        Write(output, "\n"u8);
    }

    /// <summary>An error record, the last of a cut stream (ADR 0095).</summary>
    public static void WriteError(IBufferWriter<byte> output, FeedProblemType problem)
    {
        ArgumentNullException.ThrowIfNull(output);
        Write(output, "error "u8);
        Write(output, Encoding.UTF8.GetBytes(problem.Value));
        Write(output, "\n\n"u8);
    }

    /// <summary>
    /// A record already read, written back with its terms: what the reader
    /// read, the writer writes byte for byte when the input was canonical.
    /// </summary>
    public static void WriteRecord(IBufferWriter<byte> output, FeedRecord record)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(record);

        switch (record.Kind)
        {
            case FeedRecordKind.Commit:
                WriteHeader(output, record.Position, record.CommitKind, record.Timestamp);
                WriteMetadata(output, "agent "u8, record.Agent);
                WriteMetadata(output, "cause "u8, record.Cause);
                WriteMetadata(output, "scope "u8, record.Scope);

                foreach (RdfTerm attachment in record.Attachments)
                {
                    WriteMetadata(output, "attachment "u8, attachment);
                }

                break;
            case FeedRecordKind.Diff:
                Write(output, "diff "u8);
                WriteNumber(output, record.From.Value);
                Write(output, " "u8);
                WriteNumber(output, record.Position.Value);
                Write(output, "\n"u8);
                break;
            default:
                WriteError(output, record.Problem);
                return;
        }

        foreach (FeedChange change in record.Changes)
        {
            Write(output, change.Kind == FeedChangeKind.Assert ? "+ "u8 : "- "u8);
            WriteTerm(output, change.Subject);
            Write(output, " "u8);
            WriteTerm(output, change.Predicate);
            Write(output, " "u8);
            WriteTerm(output, change.Object);

            if (change.Graph is RdfTerm graph)
            {
                Write(output, " "u8);
                WriteTerm(output, graph);
            }

            Write(output, "\n"u8);
        }

        Write(output, "\n"u8);
    }

    /// <summary>
    /// One record as a server-sent event (§3): <c>id</c> when given, <c>event</c>,
    /// and each line of the record as a <c>data:</c> line.
    /// </summary>
    public static void WriteEvent(IBufferWriter<byte> output, ReadOnlySpan<byte> eventName, FeedPosition? id, ReadOnlySpan<byte> record)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (id is FeedPosition position)
        {
            Write(output, "id: "u8);
            WriteNumber(output, position.Value);
            Write(output, "\n"u8);
        }

        Write(output, "event: "u8);
        Write(output, eventName);
        Write(output, "\n"u8);

        // The record's own lines, without the empty line that ends it: the
        // event's empty line ends both.
        while (record.Length > 0)
        {
            int end = record.IndexOf((byte)'\n');
            ReadOnlySpan<byte> line = end < 0 ? record : record[..end];
            record = end < 0 ? default : record[(end + 1)..];

            if (line.IsEmpty)
            {
                continue;
            }

            Write(output, "data: "u8);
            Write(output, line);
            Write(output, "\n"u8);
        }

        Write(output, "\n"u8);
    }

    /// <summary>One term, as canonical N-Triples writes it.</summary>
    public static void WriteTerm(IBufferWriter<byte> output, RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(term);
        int hint = 256;

        while (true)
        {
            Span<byte> span = output.GetSpan(hint);

            if (NQuadsWriter.TryWriteTerm(term, span, out int written, in Canonical))
            {
                output.Advance(written);
                return;
            }

            hint = Math.Max(hint, span.Length) * 2;
        }
    }

    private static void WriteHeader(IBufferWriter<byte> output, FeedPosition position, FeedCommitKind kind, FeedTimestamp timestamp)
    {
        Write(output, "commit "u8);
        WriteNumber(output, position.Value);
        Write(output, " "u8);
        Write(output, kind switch
        {
            FeedCommitKind.Data => "Data"u8,
            FeedCommitKind.Settings => "Settings"u8,
            _ => "Erasure"u8,
        });
        Write(output, " "u8);
        Write(output, Encoding.ASCII.GetBytes(timestamp.ToString()));
        Write(output, "\n"u8);
    }

    private static void WriteMetadata(IBufferWriter<byte> output, ReadOnlySpan<byte> keyword, TermHandle handle, TermNamer names)
    {
        if (handle.IsNone || !names(handle, out RdfTerm? term))
        {
            return;
        }

        WriteMetadata(output, keyword, term);
    }

    private static void WriteMetadata(IBufferWriter<byte> output, ReadOnlySpan<byte> keyword, RdfTerm? term)
    {
        if (term is null)
        {
            return;
        }

        Write(output, keyword);
        WriteTerm(output, term);
        Write(output, "\n"u8);
    }

    private static void WriteChanges(IBufferWriter<byte> output, QuadDelta delta, TermNamer names)
    {
        foreach (Quad quad in delta.Asserted)
        {
            WriteChange(output, "+ "u8, in quad, names);
        }

        foreach (Quad quad in delta.Retracted)
        {
            WriteChange(output, "- "u8, in quad, names);
        }
    }

    private static void WriteChange(IBufferWriter<byte> output, ReadOnlySpan<byte> sign, in Quad quad, TermNamer names)
    {
        Write(output, sign);
        WriteHandle(output, quad.Subject, names);
        Write(output, " "u8);
        WriteHandle(output, quad.Predicate, names);
        Write(output, " "u8);
        WriteHandle(output, quad.Object, names);

        if (!quad.Graph.IsNone)
        {
            Write(output, " "u8);
            WriteHandle(output, quad.Graph, names);
        }

        Write(output, "\n"u8);
    }

    private static void WriteHandle(IBufferWriter<byte> output, TermHandle handle, TermNamer names)
    {
        if (!names(handle, out RdfTerm? term))
        {
            throw new InvalidOperationException("A change names a handle its namer cannot name; a store's dictionary is append-only, so this is a defect.");
        }

        WriteTerm(output, term);
    }

    private static void WriteNumber(IBufferWriter<byte> output, long value)
    {
        Span<byte> span = output.GetSpan(20);
        Utf8Formatter.TryFormat(value, span, out int written);
        output.Advance(written);
    }

    private static void Write(IBufferWriter<byte> output, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(output.GetSpan(bytes.Length));
        output.Advance(bytes.Length);
    }
}
