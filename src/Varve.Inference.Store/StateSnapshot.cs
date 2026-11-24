// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Inference.Store.Model;
using Varve.Rdf;

namespace Varve.Inference.Store;

/// <summary>
/// The bytes of a projection's state in the local cache (ADR 0195 point 5),
/// inside the dataset's checked envelope (ADR 0213): the binding's identity,
/// the positions, the projection's own terms, and the rows, open and closed.
/// </summary>
/// <remarks>
/// The rows name the dataset's handles, which the envelope binds to the
/// dataset and to the log at the state's position; the projection's own
/// handles are their place in the terms table, which is read back in order.
/// </remarks>
[Contract(typeof(TheInferenceProjection.CheckedStateInTheLocalCache), Role = "a projection's state, as bytes")]
internal static class StateSnapshot
{
    /// <summary>The format: "VIP" and its version.</summary>
    internal static ReadOnlySpan<byte> Magic => "VIP1"u8;

    /// <summary>The directory a binding's states are under: <c>inference/{enc(binding)}/</c>, with <c>~</c> encoded too, which no backend takes in a name.</summary>
    [DesignDecision(typeof(TheInferenceProjection.CheckedStateInTheLocalCache), Scope = ExceptionScope.Boundary)]
    internal static string Directory(InferenceBinding binding)
    {
        StringBuilder text = new("inference/");
        foreach (byte b in binding.Binding.Lexical)
        {
            _ = b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'.' or (byte)'_'
                ? text.Append((char)b)
                : text.Append('%').Append("0123456789ABCDEF"[b >> 4]).Append("0123456789ABCDEF"[b & 0xF]);
        }

        return text.Append('/').ToString();
    }

    /// <summary>The name of the state at <paramref name="position"/>: names sort by position.</summary>
    [DesignDecision(typeof(TheInferenceProjection.CheckedStateInTheLocalCache), Scope = ExceptionScope.Boundary)]
    internal static string Name(InferenceBinding binding, long position) =>
        Directory(binding) + "state-" + position.ToString("D19", CultureInfo.InvariantCulture);

    /// <summary>The binding's identity: what a state must match to be read.</summary>
    internal static void Identity(ArrayBufferWriter<byte> writer, InferenceBinding binding)
    {
        Term(writer, binding.Binding);
        Byte(writer, (byte)binding.Profile);
        Byte(writer, binding.CrossGraph ? (byte)1 : (byte)0);
        Count(writer, binding.TBoxGraphs.Count);
        foreach (RdfTerm graph in binding.TBoxGraphs)
        {
            Term(writer, graph);
        }

        Byte(writer, binding.DataGraphs.IsAll ? (byte)1 : (byte)0);
        Byte(writer, binding.DataGraphs.IncludesDefault ? (byte)1 : (byte)0);
        Count(writer, binding.DataGraphs.Graphs.Count);
        foreach (RdfTerm graph in binding.DataGraphs.Graphs)
        {
            Term(writer, graph);
        }

        Count(writer, binding.DataGraphs.Prefixes.Count);
        foreach (string prefix in binding.DataGraphs.Prefixes)
        {
            Blob(writer, Encoding.UTF8.GetBytes(prefix));
        }
    }

    [DesignDecision(typeof(TheInferenceProjection.CheckedStateInTheLocalCache), Scope = ExceptionScope.Boundary)]
    internal static void Byte(IBufferWriter<byte> writer, byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }

    [DesignDecision(typeof(TheInferenceProjection.CheckedStateInTheLocalCache), Scope = ExceptionScope.Boundary)]
    internal static void Long(IBufferWriter<byte> writer, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(writer.GetSpan(8), value);
        writer.Advance(8);
    }

    [DesignDecision(typeof(TheInferenceProjection.CheckedStateInTheLocalCache), Scope = ExceptionScope.Boundary)]
    internal static void Handle(IBufferWriter<byte> writer, TermHandle handle) => Long(writer, unchecked((long)handle.Value));

    internal static void Fact(IBufferWriter<byte> writer, Fact fact)
    {
        Handle(writer, fact.Subject);
        Handle(writer, fact.Predicate);
        Handle(writer, fact.Object);
    }

    [DesignDecision(typeof(TheInferenceProjection.CheckedStateInTheLocalCache), Scope = ExceptionScope.Boundary)]
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

    /// <summary>A reader over a state's bytes; every overrun is <see cref="InvalidDataException"/>.</summary>
    internal ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _at;

        internal readonly bool AtEnd => _at == _bytes.Length;

        internal bool Header(InferenceBinding binding)
        {
            if (!Take(Magic.Length).SequenceEqual(Magic))
            {
                return false;
            }

            ArrayBufferWriter<byte> identity = new();
            Identity(identity, binding);
            return Blob().SequenceEqual(identity.WrittenSpan);
        }

        [DesignDecision(typeof(TheInferenceProjection.CheckedStateInTheLocalCache), Scope = ExceptionScope.Boundary)]
        internal byte Byte() => Take(1)[0];

        [DesignDecision(typeof(TheInferenceProjection.CheckedStateInTheLocalCache), Scope = ExceptionScope.Boundary)]
        internal long Long() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

        internal TermHandle Handle() => new(unchecked((ulong)Long()));

        internal Fact Fact()
        {
            TermHandle s = Handle(), p = Handle(), o = Handle();
            return new Fact(s, p, o);
        }

        [DesignDecision(typeof(TheInferenceProjection.CheckedStateInTheLocalCache), Scope = ExceptionScope.Boundary)]
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

        private ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || _bytes.Length - _at < length)
            {
                throw new InvalidDataException("An inference state ends early.");
            }

            ReadOnlySpan<byte> taken = _bytes.Slice(_at, length);
            _at += length;
            return taken;
        }
    }
}
