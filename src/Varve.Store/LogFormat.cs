// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// Format version 1 of <c>log/</c> (ADR 0072), tabulated in
/// <c>docs/spec/storage-format.md</c>. Nothing outside this file and the log
/// reader and writer knows its layout. From the first prerelease that writes
/// it, every byte here is read for ever.
/// </summary>
internal static class LogFormat
{
    internal const ushort Version = 1;
    internal const int HashLength = 32;
    internal const int ManifestLength = 96;
    internal const int SegmentHeaderLength = 104;
    internal const int TrailerLength = 104;
    internal const int RecordHeaderLength = 128;
    internal const byte ClosingFlag = 1;

    internal const byte TrailerClosed = 1;
    internal const byte TrailerAbandoned = 2;

    internal const byte ChunkAllocations = 1;
    internal const byte ChunkAsserted = 2;
    internal const byte ChunkRetracted = 3;

    private const byte TermIri = 0;
    private const byte TermBlank = 1;
    private const byte TermLiteral = 2;
    private const byte TermTriple = 3;
    private const byte TermPrivate = 4;

    private const ushort InlineSetVersion = 1;
    private const ushort HashAlgorithmSha256 = 1;

    internal const byte SettingDefaultAccessScope = 1;
    internal const byte SettingErasureMode = 2;

    internal const int KeyIdLength = 16;
    private const int SyntheticIvLength = 32;

    private static ReadOnlySpan<byte> ManifestMagic => "VRVM"u8;

    private static ReadOnlySpan<byte> SegmentMagic => "VRVL"u8;

    private static ReadOnlySpan<byte> TrailerMagic => "VRVT"u8;

    /// <summary>
    /// <c>prev</c> at position 1, and the header hash "before" the first
    /// commit: the hash of a domain-separated constant (ADR 0072, as ADR 0045).
    /// </summary>
    /// <remarks>
    /// Computed on each call rather than held in a static array any caller
    /// could write to (DD0004).
    /// </remarks>
    internal static byte[] Genesis() => SHA256.HashData("Varve log, before position 1"u8);

    // ---------------------------------------------------------------------------------------------
    // The manifest.

    internal static byte[] EncodeManifest(DatasetId dataset)
    {
        byte[] bytes = new byte[ManifestLength];
        Span<byte> span = bytes;
        ManifestMagic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], Version);
        dataset.WriteTo(span.Slice(8, DatasetId.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(span[24..], InlineSetVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(span[26..], HashAlgorithmSha256);
        SHA256.HashData(span[24..32], span.Slice(32, HashLength));
        SHA256.HashData(span[..64], span.Slice(64, HashLength));
        return bytes;
    }

    /// <summary>The dataset a manifest names. Refuses a manifest this build cannot read.</summary>
    /// <exception cref="UnsupportedFormatException">The manifest is of a later format version.</exception>
    /// <exception cref="LogVerificationException">The manifest is not one.</exception>
    internal static DatasetId DecodeManifest(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || !bytes[..4].SequenceEqual(ManifestMagic))
        {
            throw new LogVerificationException(0, "log/MANIFEST is not a Varve manifest.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);

        if (version > Version)
        {
            throw new UnsupportedFormatException(new FormatVersion(version), FormatVersion.Current);
        }

        if (bytes.Length != ManifestLength
            || version != Version
            || !Reserved(bytes[6..8])
            || !Reserved(bytes[28..32])
            || !HashMatches(bytes[..64], bytes.Slice(64, HashLength))
            || !HashMatches(bytes[24..32], bytes.Slice(32, HashLength)))
        {
            throw new LogVerificationException(0, "log/MANIFEST does not verify.");
        }

        ushort inlineSet = BinaryPrimitives.ReadUInt16LittleEndian(bytes[24..]);
        ushort hash = BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]);

        if (inlineSet != InlineSetVersion || hash != HashAlgorithmSha256)
        {
            throw new UnsupportedFormatException(
                "The dataset was created with inline set " + inlineSet + " and hash algorithm " + hash
                + "; this version of Varve reads inline set " + InlineSetVersion + " with SHA-256 (1).");
        }

        return DatasetId.Read(bytes.Slice(8, DatasetId.Length));
    }

    // ---------------------------------------------------------------------------------------------
    // Segment headers and trailers.

    internal static byte[] EncodeSegmentHeader(DatasetId dataset, int segment, long firstPosition, ReadOnlySpan<byte> previous)
    {
        byte[] bytes = new byte[SegmentHeaderLength];
        Span<byte> span = bytes;
        SegmentMagic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], Version);
        dataset.WriteTo(span.Slice(8, DatasetId.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)segment);
        BinaryPrimitives.WriteUInt64LittleEndian(span[32..], (ulong)firstPosition);
        previous.CopyTo(span.Slice(40, HashLength));
        SHA256.HashData(span[..72], span.Slice(72, HashLength));
        return bytes;
    }

    /// <summary>
    /// A segment header, when the bytes are one: magic, version, the dataset,
    /// the segment's own id and the self-hash all verify.
    /// </summary>
    /// <exception cref="UnsupportedFormatException">The header verifies and is of a later version.</exception>
    internal static bool TryDecodeSegmentHeader(ReadOnlySpan<byte> bytes, DatasetId dataset, int segment, out long firstPosition, out byte[] previous)
    {
        firstPosition = 0;
        previous = [];

        if (bytes.Length < SegmentHeaderLength
            || !bytes[..4].SequenceEqual(SegmentMagic)
            || !HashMatches(bytes[..72], bytes.Slice(72, HashLength)))
        {
            return false;
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);

        if (version > Version)
        {
            throw new UnsupportedFormatException(new FormatVersion(version), FormatVersion.Current);
        }

        if (version != Version || !Reserved(bytes[6..8]) || !Reserved(bytes[28..32]))
        {
            return false;
        }

        DatasetId owner = DatasetId.Read(bytes.Slice(8, DatasetId.Length));
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]);

        if (owner != dataset || id != (uint)segment)
        {
            // A whole, verified header that is not this log's: never a torn write.
            throw new LogVerificationException(0, "Segment " + segment + " is segment " + id + " of dataset " + owner + ", not of dataset " + dataset + ".");
        }

        firstPosition = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]);
        previous = bytes.Slice(40, HashLength).ToArray();
        return true;
    }

    internal static byte[] EncodeTrailer(DatasetId dataset, int segment, byte status, long head, ReadOnlySpan<byte> headHash)
    {
        byte[] bytes = new byte[TrailerLength];
        Span<byte> span = bytes;
        TrailerMagic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], Version);
        span[6] = status;
        dataset.WriteTo(span.Slice(8, DatasetId.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)segment);
        BinaryPrimitives.WriteUInt64LittleEndian(span[32..], (ulong)head);
        headHash.CopyTo(span.Slice(40, HashLength));
        SHA256.HashData(span[..72], span.Slice(72, HashLength));
        return bytes;
    }

    /// <summary>The trailer at the end of a segment's bytes, when there is one.</summary>
    internal static bool TryDecodeTrailer(ReadOnlySpan<byte> segmentBytes, DatasetId dataset, int segment, out byte status, out long head, out byte[] headHash)
    {
        status = 0;
        head = 0;
        headHash = [];

        if (segmentBytes.Length < TrailerLength)
        {
            return false;
        }

        ReadOnlySpan<byte> bytes = segmentBytes[^TrailerLength..];

        if (!bytes[..4].SequenceEqual(TrailerMagic)
            || !HashMatches(bytes[..72], bytes.Slice(72, HashLength))
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) != Version
            || bytes[6] is not (TrailerClosed or TrailerAbandoned)
            || bytes[7] != 0
            || !Reserved(bytes[28..32])
            || DatasetId.Read(bytes.Slice(8, DatasetId.Length)) != dataset
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]) != (uint)segment)
        {
            return false;
        }

        status = bytes[6];
        head = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]);
        headHash = bytes.Slice(40, HashLength).ToArray();
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Records.

    internal static void WriteRecordHeader(
        Span<byte> destination, ReadOnlySpan<byte> body, bool closing, CommitKind kind, long position, int index, ReadOnlySpan<byte> previous)
    {
        destination[..RecordHeaderLength].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)body.Length);
        destination[4] = (byte)kind;
        destination[5] = closing ? ClosingFlag : (byte)0;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], (ulong)position);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], (uint)index);
        previous.CopyTo(destination.Slice(24, HashLength));
        SHA256.HashData(body, destination.Slice(56, HashLength));
        SHA256.HashData(destination[..96], destination.Slice(96, HashLength));
    }

    /// <summary>
    /// A record header that verifies against its own hash, or null when the
    /// bytes are not one: torn, never written, or garbage.
    /// </summary>
    /// <exception cref="LogVerificationException">The header verifies and says something no writer writes.</exception>
    internal static RecordHeader? TryReadRecordHeader(ReadOnlySpan<byte> bytes, long expected)
    {
        if (bytes.Length < RecordHeaderLength || !HashMatches(bytes[..96], bytes.Slice(96, HashLength)))
        {
            return null;
        }

        byte kind = bytes[4];
        byte flags = bytes[5];

        if ((flags & ~ClosingFlag) != 0
            || kind > (byte)CommitKind.Settings
            || !Reserved(bytes[6..8])
            || !Reserved(bytes[20..24])
            || !Reserved(bytes[88..96]))
        {
            throw new LogVerificationException(expected, "A record header at position " + expected + " has unknown flags, kind or reserved bits.");
        }

        return new RecordHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            (CommitKind)kind,
            (flags & ClosingFlag) != 0,
            (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]),
            bytes.Slice(24, HashLength).ToArray(),
            bytes.Slice(56, HashLength).ToArray());
    }

    /// <summary>
    /// Whether a record header begins these bytes — its self-hash verifies — and
    /// the position it claims. Used to look past a break for bytes written after
    /// it (storage format §5, I6), where a self-hash is what tells a record from
    /// any other bytes.
    /// </summary>
    internal static bool IsRecordHeader(ReadOnlySpan<byte> bytes, out long position)
    {
        position = bytes.Length < RecordHeaderLength ? 0 : (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        return bytes.Length >= RecordHeaderLength && HashMatches(bytes[..96], bytes.Slice(96, HashLength));
    }

    /// <summary>
    /// Whether these bytes are a trailer of this segment that no longer
    /// verifies: two of its magic, dataset id and segment id agree, which one
    /// changed byte cannot undo and the start of a record never matches.
    /// </summary>
    internal static bool IsDamagedTrailer(ReadOnlySpan<byte> bytes, DatasetId dataset, int segment)
    {
        if (bytes.Length != TrailerLength)
        {
            return false;
        }

        int agree = (bytes[..4].SequenceEqual(TrailerMagic) ? 1 : 0)
            + (DatasetId.Read(bytes.Slice(8, DatasetId.Length)) == dataset ? 1 : 0)
            + (BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]) == (uint)segment ? 1 : 0);
        return agree >= 2;
    }

    // ---------------------------------------------------------------------------------------------
    // Bodies.

    /// <summary>The body — chunks of <c>alloc</c>, <c>A</c> and <c>R</c>; what <c>content</c> hashes.</summary>
    internal static byte[] EncodeBody(ReadOnlySpan<Allocation> allocations, ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted)
    {
        ArrayBufferWriter<byte> writer = new(64 + ((asserted.Length + retracted.Length) * 16));

        if (!allocations.IsEmpty)
        {
            WriteByte(writer, ChunkAllocations);
            WriteUleb(writer, (ulong)allocations.Length);

            foreach (Allocation allocation in allocations)
            {
                WriteAllocation(writer, in allocation);
            }
        }

        WriteQuadChunk(writer, ChunkAsserted, asserted);
        WriteQuadChunk(writer, ChunkRetracted, retracted);
        return writer.WrittenSpan.ToArray();
    }

    internal static void WriteAllocation(IBufferWriter<byte> writer, in Allocation allocation)
    {
        WriteUleb(writer, allocation.Id);

        if (TermIds.ClassOf(allocation.Id) == IdClass.Blank)
        {
            WriteByte(writer, TermBlank);
            return;
        }

        if (allocation.IsTriple)
        {
            WriteByte(writer, TermTriple);
            WriteUleb(writer, allocation.Subject);
            WriteUleb(writer, allocation.Predicate);
            WriteUleb(writer, allocation.Object);
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

    private static void WriteQuadChunk(ArrayBufferWriter<byte> writer, byte tag, ReadOnlySpan<Quad> quads)
    {
        if (quads.IsEmpty)
        {
            return;
        }

        WriteByte(writer, tag);
        WriteUleb(writer, (ulong)quads.Length);
        Span<byte> span = writer.GetSpan(quads.Length * 4 * MaxUlebLength);
        writer.Advance(EncodeQuads(quads, span));
    }

    /// <summary>The quads, four varints each, into a span the commit's writer gave.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static int EncodeQuads(ReadOnlySpan<Quad> quads, Span<byte> span)
    {
        int at = 0;

        for (int i = 0; i < quads.Length; i++)
        {
            at += Uleb(span[at..], quads[i].Subject.Value);
            at += Uleb(span[at..], quads[i].Predicate.Value);
            at += Uleb(span[at..], quads[i].Object.Value);
            at += Uleb(span[at..], quads[i].Graph.Value);
        }

        return at;
    }

    /// <summary>A whole commit body, decoded and checked for order.</summary>
    /// <exception cref="LogVerificationException">The body is not one this format writes.</exception>
    internal static DecodedBody DecodeBody(ReadOnlySpan<byte> bytes, long position)
    {
        Reader reader = new(bytes, position);
        List<Allocation> allocations = [];
        List<Quad> asserted = [];
        List<Quad> retracted = [];
        byte last = 0;

        while (reader.Remaining > 0)
        {
            byte tag = reader.Byte();

            if (tag is < ChunkAllocations or > ChunkRetracted || tag < last)
            {
                throw reader.Fail("unknown or out-of-order chunk");
            }

            last = tag;
            ulong count = reader.Uleb();

            if (count > (ulong)reader.Remaining)
            {
                throw reader.Fail("chunk count exceeds the body");
            }

            if (tag == ChunkAllocations)
            {
                for (ulong i = 0; i < count; i++)
                {
                    allocations.Add(ReadAllocation(ref reader));
                }
            }
            else
            {
                List<Quad> target = tag == ChunkAsserted ? asserted : retracted;
                int start = target.Count;

                for (ulong i = 0; i < count; i++)
                {
                    target.Add(default);
                }

                DecodeQuads(ref reader, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(target)[start..]);
            }
        }

        if (!Ascending(asserted) || !Ascending(retracted))
        {
            throw reader.Fail("quads out of order");
        }

        return new DecodedBody([.. allocations], [.. asserted], [.. retracted]);
    }

    /// <summary>The quads, four varints each, into the commit's list.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static void DecodeQuads(ref Reader reader, Span<Quad> quads)
    {
        for (int i = 0; i < quads.Length; i++)
        {
            quads[i] = new Quad(
                new TermHandle(reader.Uleb()),
                new TermHandle(reader.Uleb()),
                new TermHandle(reader.Uleb()),
                new TermHandle(reader.Uleb()));
        }
    }

    private static bool Ascending(List<Quad> quads)
    {
        for (int i = 1; i < quads.Count; i++)
        {
            if (Compare(quads[i - 1], quads[i]) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The order of <c>A</c> and <c>R</c> in a body: by id, <c>(s, p, o, g)</c>.</summary>
    internal static int Compare(Quad left, Quad right)
    {
        int c = left.Subject.Value.CompareTo(right.Subject.Value);

        if (c == 0)
        {
            c = left.Predicate.Value.CompareTo(right.Predicate.Value);
        }

        if (c == 0)
        {
            c = left.Object.Value.CompareTo(right.Object.Value);
        }

        return c != 0 ? c : left.Graph.Value.CompareTo(right.Graph.Value);
    }

    internal static Allocation ReadAllocation(ref Reader reader)
    {
        ulong id = reader.Uleb();
        byte kind = reader.Byte();

        switch (kind)
        {
            case TermBlank when TermIds.ClassOf(id) == IdClass.Blank:
                return new Allocation(id, null);

            case TermIri when TermIds.ClassOf(id) == IdClass.Canonical:
                try
                {
                    return new Allocation(id, RdfTerm.Iri(reader.Bytes()));
                }
                catch (ArgumentException error)
                {
                    throw reader.Fail("malformed IRI: " + error.Message);
                }

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
                return new Allocation(id, null, reader.Uleb(), reader.Uleb(), reader.Uleb());

            case TermPrivate when TermIds.ClassOf(id) == IdClass.Private:
                // The layout is fixed now (ADR 0074) so that erasure mode is not a
                // format change; nothing in this version can read the entry.
                reader.Fixed(KeyIdLength);
                reader.Fixed(SyntheticIvLength);
                reader.Bytes();
                throw reader.Fail("a private term, which needs erasure mode, and this version of Varve does not support erasure mode");

            default:
                throw reader.Fail("unknown or misclassed term entry");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Commit headers.

    /// <summary>The commit header — what the chain hashes.</summary>
    internal static byte[] EncodeHeader(in CommitHeader header)
    {
        ArrayBufferWriter<byte> writer = new(200);
        Span<byte> fixedPart = writer.GetSpan(72);
        fixedPart[..72].Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(fixedPart, Version);
        fixedPart[2] = (byte)header.Kind;
        BinaryPrimitives.WriteUInt64LittleEndian(fixedPart[4..], (ulong)header.Position);
        BinaryPrimitives.WriteInt64LittleEndian(fixedPart[12..], header.TimestampTicks);
        BinaryPrimitives.WriteUInt64LittleEndian(fixedPart[20..], header.Agent);
        BinaryPrimitives.WriteUInt64LittleEndian(fixedPart[28..], header.Cause);
        BinaryPrimitives.WriteUInt64LittleEndian(fixedPart[36..], header.GraphScope);
        BinaryPrimitives.WriteUInt64LittleEndian(fixedPart[44..], (ulong)header.CanonicalCount);
        BinaryPrimitives.WriteUInt64LittleEndian(fixedPart[52..], (ulong)header.BlankCount);
        BinaryPrimitives.WriteUInt64LittleEndian(fixedPart[60..], (ulong)header.PrivateCount);
        BinaryPrimitives.WriteUInt32LittleEndian(fixedPart[68..], (uint)header.Attachments.Length);
        writer.Advance(72);

        foreach (ulong attachment in header.Attachments)
        {
            WriteUInt64(writer, attachment);
        }

        WriteUInt32(writer, (uint)header.KindPayload.Length);
        writer.Write(header.KindPayload);
        writer.Write(header.Previous);
        writer.Write(header.Content);
        return writer.WrittenSpan.ToArray();
    }

    /// <exception cref="LogVerificationException">The header is not one this format writes.</exception>
    internal static CommitHeader DecodeHeader(ReadOnlySpan<byte> bytes, long position)
    {
        Reader reader = new(bytes, position);

        if (reader.UInt16() != Version)
        {
            throw reader.Fail("unknown commit header version");
        }

        CommitKind kind = (CommitKind)reader.Byte();

        if (kind > CommitKind.Settings || reader.Byte() != 0)
        {
            throw reader.Fail("unknown commit kind");
        }

        long headerPosition = (long)reader.UInt64();
        long ticks = (long)reader.UInt64();
        ulong agent = reader.UInt64();
        ulong cause = reader.UInt64();
        ulong scope = reader.UInt64();
        long canonical = (long)reader.UInt64();
        long blank = (long)reader.UInt64();
        long privateCount = (long)reader.UInt64();
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

        uint payloadLength = reader.UInt32();

        if (payloadLength > (uint)reader.Remaining)
        {
            throw reader.Fail("kind payload runs past the header");
        }

        byte[] payload = reader.Fixed((int)payloadLength).ToArray();
        byte[] previous = reader.Fixed(HashLength).ToArray();
        byte[] content = reader.Fixed(HashLength).ToArray();
        reader.End();

        if (canonical < 0 || blank < 0 || privateCount < 0 || ticks < 0)
        {
            throw reader.Fail("a counter or the timestamp is out of range");
        }

        if (privateCount != 0)
        {
            throw reader.Fail("private terms, which need erasure mode, and this version of Varve does not support erasure mode");
        }

        if (kind == CommitKind.Erasure)
        {
            if (payload.Length != KeyIdLength)
            {
                throw reader.Fail("an erasure commit's payload is not a key id");
            }
        }

        return new CommitHeader(kind, headerPosition, ticks, agent, cause, scope, canonical, blank, privateCount, attachments, payload, previous, content);
    }

    // ---------------------------------------------------------------------------------------------
    // Kind payloads.

    /// <summary>The settings a <see cref="CommitKind.Settings"/> commit changes, as its kind payload.</summary>
    internal static byte[] EncodeSettings(SettingsChange change)
    {
        ArrayBufferWriter<byte> writer = new(8);
        byte fields = change.DefaultAccessScope.HasValue ? (byte)1 : (byte)0;
        WriteByte(writer, fields);

        if (change.DefaultAccessScope is AccessScope scope)
        {
            WriteByte(writer, SettingDefaultAccessScope);
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
            byte value = reader.Byte();

            switch (field)
            {
                case SettingDefaultAccessScope when value <= (byte)AccessScope.Current:
                    scope = (AccessScope)value;
                    break;

                case SettingDefaultAccessScope:
                    throw reader.Fail("unknown access scope");

                case SettingErasureMode when value == 0:
                    // Off, which is what it always is in this version.
                    break;

                case SettingErasureMode:
                    throw reader.Fail("erasure mode switched on, which this version of Varve does not support");

                default:
                    throw reader.Fail("unknown settings field");
            }
        }

        reader.End();
        return new DatasetSettings(scope);
    }

    // ---------------------------------------------------------------------------------------------
    // Primitives.

    internal const int MaxUlebLength = 10;

    internal static bool HashMatches(ReadOnlySpan<byte> covered, ReadOnlySpan<byte> stored)
    {
        Span<byte> hash = stackalloc byte[HashLength];
        SHA256.HashData(covered, hash);
        return hash.SequenceEqual(stored);
    }

    private static bool Reserved(ReadOnlySpan<byte> bytes) => !bytes.ContainsAnyExcept((byte)0);

    /// <summary>An unsigned LEB128 varint into a span; returns its length.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static int Uleb(Span<byte> destination, ulong value)
    {
        int at = 0;

        while (value >= 0x80)
        {
            destination[at++] = (byte)(value | 0x80);
            value >>= 7;
        }

        destination[at++] = (byte)value;
        return at;
    }

    internal static void WriteUleb(IBufferWriter<byte> writer, ulong value) => writer.Advance(Uleb(writer.GetSpan(MaxUlebLength), value));

    internal static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }

    internal static void WriteUInt32(IBufferWriter<byte> writer, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(writer.GetSpan(4), value);
        writer.Advance(4);
    }

    internal static void WriteUInt64(IBufferWriter<byte> writer, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(writer.GetSpan(8), value);
        writer.Advance(8);
    }

    internal static void WriteBytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> bytes)
    {
        WriteUleb(writer, (ulong)bytes.Length);
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

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal readonly int Remaining => _bytes.Length - _at;

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal byte Byte() => Fixed(1)[0];

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Fixed(2));

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Fixed(4));

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Fixed(8));

        /// <summary>An unsigned LEB128 varint, refusing one longer than ten bytes or not minimal.</summary>
        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal ulong Uleb()
        {
            ulong value = 0;

            for (int shift = 0, i = 0; i < MaxUlebLength; i++, shift += 7)
            {
                byte b = Byte();

                if (i == MaxUlebLength - 1 && b > 1)
                {
                    throw Fail("a varint overflows 64 bits");
                }

                value |= (ulong)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                {
                    if (b == 0 && i > 0)
                    {
                        throw Fail("a varint is not minimal");
                    }

                    return value;
                }
            }

            throw Fail("a varint is longer than ten bytes");
        }

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal ReadOnlySpan<byte> Bytes()
        {
            ulong length = Uleb();

            if (length > (ulong)Remaining)
            {
                throw Fail("length runs past the end");
            }

            return Fixed((int)length);
        }

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
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

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
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

/// <summary>A record header that verified against its own hash.</summary>
internal sealed class RecordHeader
{
    internal RecordHeader(uint bodyLength, CommitKind kind, bool closing, long position, uint index, byte[] previous, byte[] content)
    {
        BodyLength = bodyLength;
        Kind = kind;
        Closing = closing;
        Position = position;
        Index = index;
        Previous = previous;
        Content = content;
    }

    internal uint BodyLength { get; }

    internal CommitKind Kind { get; }

    internal bool Closing { get; }

    internal long Position { get; }

    internal uint Index { get; }

    internal byte[] Previous { get; }

    internal byte[] Content { get; }
}

/// <summary>A commit body, decoded: <c>(alloc, A, R)</c>.</summary>
internal readonly struct DecodedBody
{
    internal DecodedBody(Allocation[] allocations, Quad[] asserted, Quad[] retracted)
    {
        Allocations = allocations;
        Asserted = asserted;
        Retracted = retracted;
    }

    internal Allocation[] Allocations { get; }

    internal Quad[] Asserted { get; }

    internal Quad[] Retracted { get; }
}

/// <summary>A commit header: <c>(pos, kind, meta, prev, content)</c> (spec §1), with the dictionary's counters (ADR 0072).</summary>
internal readonly struct CommitHeader
{
    internal CommitHeader(
        CommitKind kind,
        long position,
        long timestampTicks,
        ulong agent,
        ulong cause,
        ulong graphScope,
        long canonicalCount,
        long blankCount,
        long privateCount,
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
        CanonicalCount = canonicalCount;
        BlankCount = blankCount;
        PrivateCount = privateCount;
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

    /// <summary>The dictionary's canonical counter after this commit.</summary>
    internal long CanonicalCount { get; }

    /// <summary>The dictionary's blank counter after this commit.</summary>
    internal long BlankCount { get; }

    /// <summary>The dictionary's private counter after this commit; zero until erasure mode.</summary>
    internal long PrivateCount { get; }

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
