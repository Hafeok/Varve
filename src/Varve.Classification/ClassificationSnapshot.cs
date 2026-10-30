// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Classification.Model;
using Varve.Rdf;
using Generation = Varve.Classification.ClassificationProjection.Generation;
using Interval = Varve.Classification.ClassificationProjection.Interval;

namespace Varve.Classification;

/// <summary>
/// The classification projection's state as one blob of the local cache
/// (ADR 0233 point 6), kept in the dataset's checked envelope (ADR 0213),
/// which verifies every block and binds it to the dataset and the commit at
/// its position: inside, the format, the binding's identity and the state.
/// A blob that does not verify, or names another binding or format, is a
/// miss and never a wrong row (#146). The mark is <c>VCP2</c>: a state
/// written before the envelope, under <c>VCP1</c>, is never read as one.
/// </summary>
internal static class ClassificationSnapshot
{
    private const uint Magic = 0x32504356; // "VCP2"

    internal sealed record State(
        long Position,
        long Floor,
        List<Generation> Generations,
        SetTable Sets,
        List<(TermHandle Node, Interval[] Intervals)> Rows,
        List<(long At, TermHandle[] Nodes)> Changed);

    /// <summary>The directory of a binding's states, <c>classification/{enc(binding)}/</c>.</summary>
    /// <remarks>
    /// <c>enc</c> leaves <c>~</c>, which RFC 3986 calls unreserved, but no
    /// backend takes it in a blob name, and the browser's is its own
    /// generation separator; in the name it is <c>%7E</c>.
    /// </remarks>
    internal static string Directory(ClassifierBinding binding) => "classification/" + Encode(binding.Binding.Lexical).Replace("~", "%7E", StringComparison.Ordinal) + "/";

    /// <summary>The name of the state at <paramref name="position"/>: names sort by position.</summary>
    internal static string Name(ClassifierBinding binding, long position) =>
        Directory(binding) + "state-" + position.ToString("D19", CultureInfo.InvariantCulture);

    internal static byte[] Write(
        ClassifierBinding binding,
        long position,
        long floor,
        List<Generation> generations,
        SetTable sets,
        ConcurrentDictionary<TermHandle, Interval[]> rows,
        Dictionary<long, TermHandle[]> changed)
    {
        ArrayBufferWriter<byte> writer = new();
        UInt(writer, Magic);
        Identity(writer, binding);
        Long(writer, position);
        Long(writer, floor);

        Int(writer, generations.Count);
        foreach (Generation generation in generations)
        {
            Long(writer, generation.Start);
            Int(writer, generation.Annotations is null ? -1 : 0);

            if (generation.Annotations is { } annotations)
            {
                Terms(writer, annotations.SubjectClasses);
                Terms(writer, annotations.SubjectNodes);
                Terms(writer, annotations.SubjectsOf);
                Terms(writer, annotations.Links);
                Terms(writer, annotations.Public);
                Int(writer, annotations.Problems.Count);
                foreach (ClassifierProblem problem in annotations.Problems)
                {
                    Bytes(writer, Encoding.UTF8.GetBytes(problem.Message));
                }
            }
        }

        Int(writer, sets.Count);
        for (int i = 1; i < sets.Count; i++)
        {
            Handles(writer, sets.Members(new SubjectSetId(i)));
        }

        Int(writer, rows.Count);
        foreach ((TermHandle node, Interval[] intervals) in rows)
        {
            ULong(writer, node.Value);
            Int(writer, intervals.Length);
            foreach (Interval interval in intervals)
            {
                Int(writer, interval.Set.Value);
                Long(writer, interval.Start);
                Long(writer, interval.End);
            }
        }

        Int(writer, changed.Count);
        foreach ((long at, TermHandle[] nodes) in changed)
        {
            Long(writer, at);
            Handles(writer, nodes);
        }

        return writer.WrittenSpan.ToArray();
    }

    internal static bool TryRead(ReadOnlyMemory<byte> bytes, ClassifierBinding binding, out State state)
    {
        state = null!;

        ArrayBufferWriter<byte> identity = new();
        Identity(identity, binding);
        Reader reader = new(bytes);

        try
        {
            if (reader.UInt() != Magic || !reader.Take(identity.WrittenCount).Span.SequenceEqual(identity.WrittenSpan))
            {
                return false;
            }

            long position = reader.Long();
            long floor = reader.Long();
            List<Generation> generations = [];

            for (int i = reader.Count(); i > 0; i--)
            {
                long start = reader.Long();

                if (reader.Int() < 0)
                {
                    generations.Add(new Generation(start, null));
                    continue;
                }

                Annotations annotations = Annotations.Empty();
                annotations.SubjectClasses.AddRange(reader.Terms());
                annotations.SubjectNodes.AddRange(reader.Terms());
                annotations.SubjectsOf.AddRange(reader.Terms());
                annotations.Links.AddRange(reader.Terms());
                annotations.Public.Clear();
                annotations.Public.AddRange(reader.Terms());

                for (int p = reader.Count(); p > 0; p--)
                {
                    annotations.Problems.Add(new ClassifierProblem(Encoding.UTF8.GetString(reader.Bytes().Span)));
                }

                generations.Add(new Generation(start, annotations));
            }

            SetTable sets = new();
            int count = reader.Count();
            for (int i = 1; i < count; i++)
            {
                if (sets.Intern(reader.Handles()).Value != i)
                {
                    return false;
                }
            }

            List<(TermHandle, Interval[])> rows = [];
            for (int i = reader.Count(); i > 0; i--)
            {
                TermHandle node = new(reader.ULong());
                Interval[] intervals = new Interval[reader.Count()];

                for (int j = 0; j < intervals.Length; j++)
                {
                    intervals[j] = new Interval(new SubjectSetId(reader.Int()), reader.Long(), reader.Long());

                    if (intervals[j].Set.Value <= 0 || intervals[j].Set.Value >= count)
                    {
                        return false;
                    }
                }

                rows.Add((node, intervals));
            }

            List<(long, TermHandle[])> changed = [];
            for (int i = reader.Count(); i > 0; i--)
            {
                changed.Add((reader.Long(), reader.Handles()));
            }

            if (!reader.AtEnd || generations.Count == 0 || floor > position)
            {
                return false;
            }

            state = new State(position, floor, generations, sets, rows, changed);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>Percent-encodes every byte outside RFC 3986's unreserved set, with upper-case hex: <c>enc</c>.</summary>
    [DesignDecision(typeof(TheClassificationProjection.ClassificationInTheLocalCacheVerified), Scope = ExceptionScope.Boundary)]
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

    // What a state must match to be read: the binding, its shapes graph and the graphs it classifies.
    private static void Identity(ArrayBufferWriter<byte> writer, ClassifierBinding binding)
    {
        Bytes(writer, binding.Binding.Lexical);
        Bytes(writer, binding.ShapesGraph.Lexical);
        Int(writer, binding.Classifies.IsAll ? 1 : 0);
        Int(writer, binding.Classifies.IncludesDefault ? 1 : 0);
        Int(writer, binding.Classifies.Graphs.Count);
        foreach (RdfTerm graph in binding.Classifies.Graphs)
        {
            Bytes(writer, graph.Lexical);
        }

        IReadOnlyList<string> prefixes = binding.Classifies.Prefixes;
        Int(writer, prefixes.Count);
        foreach (string prefix in prefixes)
        {
            Bytes(writer, Encoding.UTF8.GetBytes(prefix));
        }
    }

    private static void Terms(ArrayBufferWriter<byte> writer, List<RdfTerm> terms)
    {
        Int(writer, terms.Count);
        foreach (RdfTerm term in terms)
        {
            Bytes(writer, term.Lexical);
        }
    }

    private static void Handles(ArrayBufferWriter<byte> writer, ReadOnlySpan<TermHandle> handles)
    {
        Int(writer, handles.Length);
        foreach (TermHandle handle in handles)
        {
            ULong(writer, handle.Value);
        }
    }

    private static void Bytes(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> bytes)
    {
        Int(writer, bytes.Length);
        writer.Write(bytes);
    }

    private static void Int(ArrayBufferWriter<byte> writer, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), value);
        writer.Advance(4);
    }

    private static void UInt(ArrayBufferWriter<byte> writer, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(writer.GetSpan(4), value);
        writer.Advance(4);
    }

    private static void Long(ArrayBufferWriter<byte> writer, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(writer.GetSpan(8), value);
        writer.Advance(8);
    }

    private static void ULong(ArrayBufferWriter<byte> writer, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(writer.GetSpan(8), value);
        writer.Advance(8);
    }

    private sealed class Reader(ReadOnlyMemory<byte> bytes)
    {
        private int _offset;

        internal bool AtEnd => _offset == bytes.Length;

        internal ReadOnlyMemory<byte> Take(int length)
        {
            if (length < 0 || _offset + length > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            ReadOnlyMemory<byte> taken = bytes.Slice(_offset, length);
            _offset += length;
            return taken;
        }

        internal int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4).Span);

        internal int Count()
        {
            int count = Int();
            return count < 0 || count > bytes.Length ? throw new ArgumentOutOfRangeException(nameof(count)) : count;
        }

        internal uint UInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4).Span);

        internal long Long() => BinaryPrimitives.ReadInt64LittleEndian(Take(8).Span);

        internal ulong ULong() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8).Span);

        internal ReadOnlyMemory<byte> Bytes() => Take(Count());

        internal List<RdfTerm> Terms()
        {
            List<RdfTerm> terms = [];
            for (int i = Count(); i > 0; i--)
            {
                terms.Add(RdfTerm.Iri(Bytes().Span));
            }

            return terms;
        }

        internal TermHandle[] Handles()
        {
            TermHandle[] handles = new TermHandle[Count()];
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = new TermHandle(ULong());
            }

            return handles;
        }
    }
}
