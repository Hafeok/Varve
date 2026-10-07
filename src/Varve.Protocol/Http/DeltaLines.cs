// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Protocol.Http;

/// <summary>Names a handle with its term.</summary>
internal delegate bool TermNamer(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term);

/// <summary>
/// Writes <c>application/vnd.varve.delta; version=1</c> (<c>change-feed.md</c>
/// §2): commit, diff and error records, and their server-sent-events framing
/// (§3). Terms are canonical N-Triples; blank nodes keep the labels the store
/// gives them (ADR 0098).
/// </summary>
internal static class DeltaLines
{
    /// <summary>A commit record, the empty line that ends it included.</summary>
    internal static void WriteCommit(IBufferWriter<byte> output, Commit commit, QuadDelta delta, TermNamer names)
    {
        Write(output, "commit "u8);
        WriteNumber(output, commit.Position.Value);
        Write(output, " "u8);
        Write(output, commit.Kind switch
        {
            CommitKind.Data => "Data"u8,
            CommitKind.Settings => "Settings"u8,
            _ => "Erasure"u8,
        });
        Write(output, " "u8);
        Write(output, Encoding.ASCII.GetBytes(Instants.Format(commit.Timestamp)));
        Write(output, "\n"u8);
        WriteMetadata(output, "agent "u8, commit.Agent, names);
        WriteMetadata(output, "cause "u8, commit.Cause, names);
        WriteMetadata(output, "scope "u8, commit.GraphScope, names);

        foreach (TermHandle attachment in commit.Attachments.Span)
        {
            WriteMetadata(output, "attachment "u8, attachment, names);
        }

        WriteChanges(output, delta, names);
        Write(output, "\n"u8);
    }

    /// <summary>A diff record, the empty line that ends it included.</summary>
    internal static void WriteDiff(IBufferWriter<byte> output, Position from, Position to, QuadDelta delta, TermNamer names)
    {
        Write(output, "diff "u8);
        WriteNumber(output, from.Value);
        Write(output, " "u8);
        WriteNumber(output, to.Value);
        Write(output, "\n"u8);
        WriteChanges(output, delta, names);
        Write(output, "\n"u8);
    }

    /// <summary>An error record: the last of a cut stream (ADR 0095).</summary>
    internal static void WriteError(IBufferWriter<byte> output, ProblemType problem)
    {
        Write(output, "error "u8);
        Write(output, Encoding.UTF8.GetBytes(problem.Value));
        Write(output, "\n\n"u8);
    }

    /// <summary>
    /// One record as a server-sent event: <c>id</c>, <c>event</c>, and each
    /// line of the record as a <c>data:</c> line (<c>change-feed.md</c> §3).
    /// </summary>
    internal static void WriteEvent(IBufferWriter<byte> output, ReadOnlySpan<byte> eventName, Position? id, ReadOnlySpan<byte> record)
    {
        if (id is Position position)
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

    private static void WriteMetadata(IBufferWriter<byte> output, ReadOnlySpan<byte> keyword, TermHandle handle, TermNamer names)
    {
        if (handle.IsNone || !names(handle, out RdfTerm? term))
        {
            return;
        }

        Write(output, keyword);
        TermLines.WriteTerm(output, term);
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
            throw new InvalidOperationException("A change names a handle its commit cannot name; the dictionary is append-only, so this is a defect.");
        }

        TermLines.WriteTerm(output, term);
    }

    private static void WriteNumber(IBufferWriter<byte> output, long value)
    {
        Span<byte> span = output.GetSpan(20);
        Utf8Formatter.TryFormat(value, span, out int written);
        output.Advance(written);
    }

    private static void Write(IBufferWriter<byte> output, ReadOnlySpan<byte> bytes) => TermLines.Write(output, bytes);
}
