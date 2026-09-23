// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>
/// The provisional log encoding of ADR 0045. Every byte of it may change at
/// milestone 6; nothing outside this file knows its layout.
/// </summary>
internal static class LogFormat
{
    internal const int PreambleLength = 8;
    internal const int RecordHeaderLength = 20;
    internal const int HashLength = 32;
    internal const byte Version = 0;
    internal const byte ClosingFlag = 1;

    private const byte TermIri = 0;
    private const byte TermBlank = 1;
    private const byte TermLiteral = 2;
    private const byte TermTriple = 3;

    internal static ReadOnlySpan<byte> Preamble => "VRVL\0\0\0\0"u8;

    /// <summary>
    /// <c>prev</c> at position 1: the hash of a domain-separated constant, so
    /// that a hash of nothing, or another format's genesis, is not mistaken for
    /// it (ADR 0045).
    /// </summary>
    internal static byte[] Genesis { get; } = SHA256.HashData("Varve log, before position 1"u8);

    /// <summary>The body — <c>(alloc, A, R)</c>, what <c>content</c> hashes.</summary>
    internal static byte[] EncodeBody(ReadOnlySpan<Allocation> allocations, ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted)
    {
        ArrayBufferWriter<byte> writer = new(64 + ((asserted.Length + retracted.Length) * 32));
        WriteUInt32(writer, (uint)allocations.Length);

        foreach (Allocation allocation in allocations)
        {
            WriteAllocation(writer, in allocation);
        }

        WriteQuads(writer, asserted);
        WriteQuads(writer, retracted);
        return writer.WrittenSpan.ToArray();
    }

    internal static void WriteAllocation(ArrayBufferWriter<byte> writer, in Allocation allocation)
    {
        WriteUInt64(writer, allocation.Id);

        if (TermIds.ClassOf(allocation.Id) == IdClass.Blank)
        {
            WriteByte(writer, TermBlank);
            return;
        }

        if (allocation.IsTriple)
        {
            WriteByte(writer, TermTriple);
            WriteUInt64(writer, allocation.Subject);
            WriteUInt64(writer, allocation.Predicate);
            WriteUInt64(writer, allocation.Object);
            return;
        }

        RdfTerm term = allocation.Term!;

        if (term.Kind == RdfTermKind.Iri)
        {
            WriteByte(writer, TermIri);
            WriteBytes(writer, term.Lexical);
            return;
        }

        WriteByte(writer, TermLiteral);
        WriteBytes(writer, term.Lexical);
        WriteBytes(writer, term.Datatype is null ? default : term.Datatype.Lexical);
        WriteBytes(writer, term.Language);
        WriteByte(writer, (byte)term.Direction);
    }

    /// <summary>The header — what the chain hashes.</summary>
    internal static byte[] EncodeHeader(in CommitHeader header)
    {
        ArrayBufferWriter<byte> writer = new(160);
        WriteByte(writer, Version);
        WriteByte(writer, (byte)header.Kind);
        WriteUInt64(writer, (ulong)header.Position);
        WriteUInt64(writer, (ulong)header.TimestampTicks);
        WriteUInt64(writer, header.Agent);
        WriteUInt64(writer, header.Cause);
        WriteUInt64(writer, header.GraphScope);
        WriteUInt32(writer, (uint)header.Attachments.Length);

        foreach (ulong attachment in header.Attachments)
        {
            WriteUInt64(writer, attachment);
        }

        WriteBytes(writer, header.KindPayload);
        writer.Write(header.Previous);
        writer.Write(header.Content);
        return writer.WrittenSpan.ToArray();
    }

    internal static CommitHeader DecodeHeader(ReadOnlySpan<byte> bytes, long position)
    {
        Reader reader = new(bytes, position);

        if (reader.Byte() != Version)
        {
            throw reader.Fail("unknown header version");
        }

        CommitKind kind = (CommitKind)reader.Byte();

        if (kind > CommitKind.Settings)
        {
            throw reader.Fail("unknown commit kind");
        }

        long headerPosition = (long)reader.UInt64();
        long ticks = (long)reader.UInt64();
        ulong agent = reader.UInt64();
        ulong cause = reader.UInt64();
        ulong scope = reader.UInt64();
        uint attachmentCount = reader.UInt32();

        if (attachmentCount > bytes.Length / 8)
        {
            throw reader.Fail("attachment count exceeds the header");
        }

        ulong[] attachments = new ulong[attachmentCount];

        for (int i = 0; i < attachments.Length; i++)
        {
            attachments[i] = reader.UInt64();
        }

        byte[] payload = reader.Bytes().ToArray();
        byte[] previous = reader.Fixed(HashLength).ToArray();
        byte[] content = reader.Fixed(HashLength).ToArray();
        reader.End();

        return new CommitHeader(kind, headerPosition, ticks, agent, cause, scope, attachments, payload, previous, content);
    }

    internal static (Allocation[] Allocations, Quad[] Asserted, Quad[] Retracted) DecodeBody(ReadOnlySpan<byte> bytes, long position)
    {
        Reader reader = new(bytes, position);
        uint count = reader.UInt32();

        if (count > bytes.Length / 9)
        {
            throw reader.Fail("allocation count exceeds the body");
        }

        Allocation[] allocations = new Allocation[count];

        for (int i = 0; i < allocations.Length; i++)
        {
            allocations[i] = ReadAllocation(ref reader);
        }

        Quad[] asserted = ReadQuads(ref reader, bytes.Length);
        Quad[] retracted = ReadQuads(ref reader, bytes.Length);
        reader.End();
        return (allocations, asserted, retracted);
    }

    internal static Allocation ReadAllocation(ref Reader reader)
    {
        ulong id = reader.UInt64();
        byte kind = reader.Byte();

        switch (kind)
        {
            case TermBlank when TermIds.ClassOf(id) == IdClass.Blank:
                return new Allocation(id, null);

            case TermIri when TermIds.ClassOf(id) == IdClass.Canonical:
                return new Allocation(id, RdfTerm.Iri(reader.Bytes()));

            case TermLiteral when TermIds.ClassOf(id) == IdClass.Canonical:
                ReadOnlySpan<byte> lexical = reader.Bytes();
                ReadOnlySpan<byte> datatype = reader.Bytes();
                ReadOnlySpan<byte> language = reader.Bytes();
                byte direction = reader.Byte();

                try
                {
                    RdfTerm literal = language.Length > 0
                        ? RdfTerm.Literal(lexical, language, (TextDirection)direction)
                        : datatype.Length > 0
                            ? RdfTerm.Literal(lexical, RdfTerm.Iri(datatype))
                            : RdfTerm.Literal(lexical);
                    return new Allocation(id, literal);
                }
                catch (ArgumentException error)
                {
                    throw reader.Fail("malformed literal: " + error.Message);
                }

            case TermTriple when TermIds.ClassOf(id) == IdClass.Canonical:
                return new Allocation(id, null, reader.UInt64(), reader.UInt64(), reader.UInt64());

            default:
                throw reader.Fail("unknown or misclassed term entry");
        }
    }

    /// <summary>The settings a <see cref="CommitKind.Settings"/> commit changes, as its kind payload.</summary>
    internal static byte[] EncodeSettings(SettingsChange change)
    {
        ArrayBufferWriter<byte> writer = new(8);
        byte fields = change.DefaultAccessScope.HasValue ? (byte)1 : (byte)0;
        WriteByte(writer, fields);

        if (change.DefaultAccessScope is AccessScope scope)
        {
            WriteByte(writer, 1);
            WriteByte(writer, (byte)scope);
        }

        return writer.WrittenSpan.ToArray();
    }

    internal static DatasetSettings ApplySettings(DatasetSettings before, ReadOnlySpan<byte> payload, long position)
    {
        Reader reader = new(payload, position);
        int fields = reader.Byte();
        AccessScope scope = before.DefaultAccessScope;

        for (int i = 0; i < fields; i++)
        {
            byte field = reader.Byte();

            if (field != 1)
            {
                throw reader.Fail("unknown settings field");
            }

            byte value = reader.Byte();

            if (value > (byte)AccessScope.Current)
            {
                throw reader.Fail("unknown access scope");
            }

            scope = (AccessScope)value;
        }

        reader.End();
        return new DatasetSettings(scope);
    }

    internal static void WriteRecordHeader(Span<byte> destination, int payloadLength, bool closing, CommitKind kind, long position, int index)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)payloadLength);
        destination[4] = closing ? ClosingFlag : (byte)0;
        destination[5] = (byte)kind;
        destination[6] = 0;
        destination[7] = 0;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], (ulong)position);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], (uint)index);
    }

    private static void WriteQuads(ArrayBufferWriter<byte> writer, ReadOnlySpan<Quad> quads)
    {
        WriteUInt32(writer, (uint)quads.Length);
        Span<byte> span = writer.GetSpan(quads.Length * 32);

        for (int i = 0; i < quads.Length; i++)
        {
            Span<byte> at = span[(i * 32)..];
            BinaryPrimitives.WriteUInt64LittleEndian(at, quads[i].Subject.Value);
            BinaryPrimitives.WriteUInt64LittleEndian(at[8..], quads[i].Predicate.Value);
            BinaryPrimitives.WriteUInt64LittleEndian(at[16..], quads[i].Object.Value);
            BinaryPrimitives.WriteUInt64LittleEndian(at[24..], quads[i].Graph.Value);
        }

        writer.Advance(quads.Length * 32);
    }

    private static Quad[] ReadQuads(ref Reader reader, int limit)
    {
        uint count = reader.UInt32();

        if (count > limit / 32)
        {
            throw reader.Fail("quad count exceeds the body");
        }

        Quad[] quads = new Quad[count];

        for (int i = 0; i < quads.Length; i++)
        {
            quads[i] = new Quad(
                new TermHandle(reader.UInt64()),
                new TermHandle(reader.UInt64()),
                new TermHandle(reader.UInt64()),
                new TermHandle(reader.UInt64()));
        }

        return quads;
    }

    internal static void WriteByte(ArrayBufferWriter<byte> writer, byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }

    internal static void WriteUInt32(ArrayBufferWriter<byte> writer, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(writer.GetSpan(4), value);
        writer.Advance(4);
    }

    internal static void WriteUInt64(ArrayBufferWriter<byte> writer, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(writer.GetSpan(8), value);
        writer.Advance(8);
    }

    internal static void WriteBytes(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> bytes)
    {
        WriteUInt32(writer, (uint)bytes.Length);
        writer.Write(bytes);
    }

    /// <summary>A bounds-checked little-endian reader that refuses rather than guesses.</summary>
    internal ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private readonly long _position;
        private int _at;

        internal Reader(ReadOnlySpan<byte> bytes, long position)
        {
            _bytes = bytes;
            _position = position;
            _at = 0;
        }

        internal readonly int Remaining => _bytes.Length - _at;

        internal byte Byte() => Fixed(1)[0];

        internal uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Fixed(4));

        internal ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Fixed(8));

        internal ReadOnlySpan<byte> Bytes()
        {
            uint length = UInt32();

            if (length > (uint)Remaining)
            {
                throw Fail("length runs past the end");
            }

            return Fixed((int)length);
        }

        internal ReadOnlySpan<byte> Fixed(int length)
        {
            if (length > Remaining)
            {
                throw Fail("unexpected end");
            }

            ReadOnlySpan<byte> slice = _bytes.Slice(_at, length);
            _at += length;
            return slice;
        }

        internal readonly void End()
        {
            if (Remaining != 0)
            {
                throw Fail("trailing bytes");
            }
        }

        internal readonly LogVerificationException Fail(string reason) =>
            new(_position, "The log does not verify at position " + _position + ": " + reason + ".");
    }
}

/// <summary>A commit header: <c>(pos, kind, meta, prev, content)</c> (spec §1).</summary>
internal readonly struct CommitHeader
{
    internal CommitHeader(
        CommitKind kind,
        long position,
        long timestampTicks,
        ulong agent,
        ulong cause,
        ulong graphScope,
        ulong[] attachments,
        byte[] kindPayload,
        byte[] previous,
        byte[] content)
    {
        Kind = kind;
        Position = position;
        TimestampTicks = timestampTicks;
        Agent = agent;
        Cause = cause;
        GraphScope = graphScope;
        Attachments = attachments;
        KindPayload = kindPayload;
        Previous = previous;
        Content = content;
    }

    internal CommitKind Kind { get; }

    internal long Position { get; }

    internal long TimestampTicks { get; }

    internal ulong Agent { get; }

    internal ulong Cause { get; }

    internal ulong GraphScope { get; }

    internal ulong[] Attachments { get; }

    internal byte[] KindPayload { get; }

    internal byte[] Previous { get; }

    internal byte[] Content { get; }

    /// <summary>Every id the metadata mentions.</summary>
    internal IEnumerable<ulong> MetadataIds()
    {
        if (Agent != 0)
        {
            yield return Agent;
        }

        if (Cause != 0)
        {
            yield return Cause;
        }

        if (GraphScope != 0)
        {
            yield return GraphScope;
        }

        foreach (ulong attachment in Attachments)
        {
            yield return attachment;
        }
    }
}
