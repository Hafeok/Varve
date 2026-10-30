// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Shacl.Store.Model;

namespace Varve.Shacl.Store;

/// <summary>
/// A validation projection's state as one blob (ADR 0174): the binding it
/// projects, its position and floor, its generations and its rows, each row's
/// results as <see cref="Shacl.Model.ValidationReport.WriteTo"/> wrote them.
/// A failed generation keeps only its start: its problems are the full
/// validation's there, and are computed again when the snapshot is read.
/// Kept in the dataset's checked envelope (ADR 0213), which verifies every
/// block and binds it to the dataset and the commit at its position; the
/// layout inside is version 1's under the mark <c>VVP2</c>, so a blob
/// written before the envelope is never read as one.
/// </summary>
internal static class ProjectionSnapshot
{
    private static ReadOnlySpan<byte> Magic => "VVP2"u8;

    /// <summary>The directory a binding's snapshots are written to: <c>validation/{enc(binding)}/</c>.</summary>
    internal static string Directory(ShapesBinding binding) => "validation/" + Encode(binding.Binding.Lexical) + "/";

    /// <summary>The name of the snapshot at <paramref name="position"/>: names sort by position.</summary>
    internal static string Name(ShapesBinding binding, long position) =>
        Directory(binding) + "state-" + position.ToString("D19", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Percent-encodes every byte outside RFC 3986's unreserved set, with upper-case hex: <c>enc</c>.</summary>
    [DesignDecision(typeof(TheValidationProjection.LocalCacheOnly), Scope = ExceptionScope.Boundary)]
    internal static string Encode(ReadOnlySpan<byte> utf8)
    {
        StringBuilder text = new(utf8.Length * 3);
        foreach (byte b in utf8)
        {
            if (b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~')
            {
                text.Append((char)b);
            }
            else
            {
                text.Append('%').Append("0123456789ABCDEF"[b >> 4]).Append("0123456789ABCDEF"[b & 0xF]);
            }
        }

        return text.ToString();
    }

    /// <summary>The binding's identity: what a snapshot must match to be read.</summary>
    internal static void Identity(ArrayBufferWriter<byte> writer, ShapesBinding binding)
    {
        Term(writer, binding.Binding);
        Term(writer, binding.ShapesGraph);
        Byte(writer, binding.Validates.IsAll ? (byte)1 : (byte)0);
        Byte(writer, binding.Validates.IncludesDefault ? (byte)1 : (byte)0);
        Count(writer, binding.Validates.Graphs.Count);
        foreach (RdfTerm graph in binding.Validates.Graphs)
        {
            Term(writer, graph);
        }

        IReadOnlyList<string> prefixes = binding.Validates.Prefixes;
        Count(writer, prefixes.Count);
        foreach (string prefix in prefixes)
        {
            Blob(writer, Encoding.UTF8.GetBytes(prefix));
        }

        Byte(writer, binding.SubClassOfInShapesGraph ? (byte)1 : (byte)0);
    }

    internal static void Header(ArrayBufferWriter<byte> writer, ShapesBinding binding, long position, long floor)
    {
        writer.Write(Magic);
        ArrayBufferWriter<byte> identity = new();
        Identity(identity, binding);
        Blob(writer, identity.WrittenSpan);
        Long(writer, position);
        Long(writer, floor);
    }

    internal static void Byte(IBufferWriter<byte> writer, byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }

    internal static void Long(IBufferWriter<byte> writer, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(writer.GetSpan(8), value);
        writer.Advance(8);
    }

    internal static void Count(IBufferWriter<byte> writer, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            Byte(writer, (byte)(v | 0x80));
            v >>= 7;
        }

        Byte(writer, (byte)v);
    }

    internal static void Blob(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        Count(writer, value.Length);
        writer.Write(value);
    }

    internal static void Term(IBufferWriter<byte> writer, RdfTerm term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                Byte(writer, 0);
                Blob(writer, term.Lexical);
                break;
            case RdfTermKind.BlankNode:
                Byte(writer, 1);
                Blob(writer, term.Lexical);
                break;
            case RdfTermKind.Literal:
                Byte(writer, 2);
                Blob(writer, term.Lexical);
                Blob(writer, term.Datatype is { } datatype ? datatype.Lexical : default);
                Blob(writer, term.Language);
                Byte(writer, (byte)term.Direction);
                break;
            default:
                Byte(writer, 3);
                Term(writer, term.Subject!);
                Term(writer, term.Predicate!);
                Term(writer, term.Object!);
                break;
        }
    }

    /// <summary>A reader over a snapshot's bytes; every overrun is <see cref="InvalidDataException"/>.</summary>
    internal ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _at;

        internal readonly bool AtEnd => _at == _bytes.Length;

        internal ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || _bytes.Length - _at < length)
            {
                throw new InvalidDataException("A validation snapshot ends early.");
            }

            ReadOnlySpan<byte> taken = _bytes.Slice(_at, length);
            _at += length;
            return taken;
        }

        internal bool Header(ShapesBinding binding, out long position, out long floor)
        {
            position = floor = 0;
            if (!Take(Magic.Length).SequenceEqual(Magic))
            {
                return false;
            }

            ArrayBufferWriter<byte> identity = new();
            Identity(identity, binding);
            if (!Blob().SequenceEqual(identity.WrittenSpan))
            {
                return false;
            }

            position = Long();
            floor = Long();
            return true;
        }

        internal byte Byte() => Take(1)[0];

        internal long Long() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

        internal int Count()
        {
            uint value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = Byte();
                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return value <= int.MaxValue ? (int)value : throw new InvalidDataException("A count past the largest list.");
                }
            }

            throw new InvalidDataException("A count longer than five bytes.");
        }

        internal ReadOnlySpan<byte> Blob() => Take(Count());

        internal RdfTerm Term()
        {
            switch (Byte())
            {
                case 0:
                    return RdfTerm.Iri(Blob());
                case 1:
                    return RdfTerm.BlankNode(Blob());
                case 2:
                    ReadOnlySpan<byte> lexical = Blob();
                    ReadOnlySpan<byte> datatype = Blob();
                    ReadOnlySpan<byte> language = Blob();
                    TextDirection direction = (TextDirection)Byte();
                    return !language.IsEmpty
                        ? RdfTerm.Literal(lexical, language, direction)
                        : datatype.IsEmpty ? RdfTerm.Literal(lexical) : RdfTerm.Literal(lexical, RdfTerm.Iri(datatype));
                case 3:
                    RdfTerm s = Term(), p = Term(), o = Term();
                    return RdfTerm.TripleTerm(s, p, o);
                default:
                    throw new InvalidDataException("A term of an unknown kind.");
            }
        }
    }
}
