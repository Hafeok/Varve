// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Varve.Rdf;
using Varve.Shacl.Shapes;

namespace Varve.Shacl.Model;

/// <summary>
/// A report as bytes, for a store that keeps one (ADR 0174): the header —
/// whether the shapes are the data, the conformance-disallow set, the imports
/// not followed — and each result's terms and path, every term spelled out.
/// Derived and droppable: a version this build does not read is refused, and
/// its owner validates again.
/// </summary>
internal static class ReportCodec
{
    private static ReadOnlySpan<byte> Magic => "VSR1"u8;

    internal static void Write(IBufferWriter<byte> writer, bool shapesAreData, IReadOnlyList<RdfTerm>? disallows, IReadOnlyList<RdfTerm> imports, IReadOnlyList<ShaclResult> results, ImmutableArray<ShapePath?> paths)
    {
        Writer w = new(writer);
        w.Bytes(Magic);
        w.Byte(shapesAreData ? (byte)1 : (byte)0);
        if (disallows is null)
        {
            w.Byte(0);
        }
        else
        {
            w.Byte(1);
            w.Terms(disallows);
        }

        w.Terms(imports);
        w.Count(results.Count);
        for (int i = 0; i < results.Count; i++)
        {
            ShaclResult result = results[i];
            w.Term(result.FocusNode);
            if (paths[i] is { } path)
            {
                w.Byte(1);
                w.Path(path);
            }
            else
            {
                w.Byte(0);
            }

            w.Optional(result.Value);
            w.Term(result.SourceShape);
            w.Optional(result.SourceConstraint);
            w.Term(result.SourceConstraintComponent);
            w.Term(result.Severity);
            w.Terms(result.Messages);
        }
    }

    internal static (bool ShapesAreData, IReadOnlyList<RdfTerm>? Disallows, IReadOnlyList<RdfTerm> Imports, List<(ShaclResult Result, ShapePath? Path)> Results) Read(ReadOnlySpan<byte> bytes)
    {
        Reader r = new(bytes);
        if (!r.Take(Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a report this version of Varve reads.");
        }

        bool shapesAreData = r.Byte() == 1;
        IReadOnlyList<RdfTerm>? disallows = r.Byte() == 1 ? r.Terms() : null;
        IReadOnlyList<RdfTerm> imports = r.Terms();
        int count = r.Count();
        List<(ShaclResult, ShapePath?)> results = new(count);
        for (int i = 0; i < count; i++)
        {
            RdfTerm focus = r.Term();
            ShapePath? path = r.Byte() == 1 ? r.Path() : null;
            RdfTerm? value = r.Optional();
            RdfTerm shape = r.Term();
            RdfTerm? constraint = r.Optional();
            RdfTerm component = r.Term();
            RdfTerm severity = r.Term();
            IReadOnlyList<RdfTerm> messages = r.Terms();
            ShaclResult result = new(focus, path is PredicatePath p ? p.Predicate : null, path is not null, value, shape, component, constraint, severity, [.. messages]);
            results.Add((result, path));
        }

        if (!r.AtEnd)
        {
            throw new InvalidDataException("A report's bytes end after its last result.");
        }

        return (shapesAreData, disallows, imports, results);
    }

    private readonly ref struct Writer(IBufferWriter<byte> writer)
    {
        private readonly IBufferWriter<byte> _writer = writer;

        internal void Byte(byte value)
        {
            _writer.GetSpan(1)[0] = value;
            _writer.Advance(1);
        }

        internal void Bytes(ReadOnlySpan<byte> value) => _writer.Write(value);

        internal void Count(int value)
        {
            uint v = (uint)value;
            while (v >= 0x80)
            {
                Byte((byte)(v | 0x80));
                v >>= 7;
            }

            Byte((byte)v);
        }

        internal void Blob(ReadOnlySpan<byte> value)
        {
            Count(value.Length);
            Bytes(value);
        }

        internal void Term(RdfTerm term)
        {
            switch (term.Kind)
            {
                case RdfTermKind.Iri:
                    Byte(0);
                    Blob(term.Lexical);
                    break;
                case RdfTermKind.BlankNode:
                    Byte(1);
                    Blob(term.Lexical);
                    break;
                case RdfTermKind.Literal:
                    Byte(2);
                    Blob(term.Lexical);
                    Blob(term.Datatype is { } datatype ? datatype.Lexical : default);
                    Blob(term.Language);
                    Byte((byte)term.Direction);
                    break;
                default:
                    Byte(3);
                    Term(term.Subject!);
                    Term(term.Predicate!);
                    Term(term.Object!);
                    break;
            }
        }

        internal void Optional(RdfTerm? term)
        {
            if (term is null)
            {
                Byte(0);
                return;
            }

            Byte(1);
            Term(term);
        }

        internal void Terms(IReadOnlyList<RdfTerm> terms)
        {
            Count(terms.Count);
            foreach (RdfTerm term in terms)
            {
                Term(term);
            }
        }

        internal void Path(ShapePath path)
        {
            switch (path)
            {
                case PredicatePath predicate:
                    Byte(0);
                    Term(predicate.Predicate);
                    break;
                case SequencePath sequence:
                    Byte(1);
                    Count(sequence.Steps.Length);
                    foreach (ShapePath step in sequence.Steps)
                    {
                        Path(step);
                    }

                    break;
                case AlternativePath alternative:
                    Byte(2);
                    Count(alternative.Alternatives.Length);
                    foreach (ShapePath member in alternative.Alternatives)
                    {
                        Path(member);
                    }

                    break;
                case UnaryPath unary:
                    Byte(3);
                    Byte((byte)unary.Kind);
                    Path(unary.Inner);
                    break;
                default:
                    throw new InvalidOperationException("A path of an unknown kind.");
            }
        }
    }

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _at;

        internal readonly bool AtEnd => _at == _bytes.Length;

        internal ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || _bytes.Length - _at < length)
            {
                throw new InvalidDataException("A report's bytes end early.");
            }

            ReadOnlySpan<byte> taken = _bytes.Slice(_at, length);
            _at += length;
            return taken;
        }

        internal byte Byte() => Take(1)[0];

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

        internal RdfTerm? Optional() => Byte() == 1 ? Term() : null;

        internal List<RdfTerm> Terms()
        {
            int count = Count();
            List<RdfTerm> terms = new(Math.Min(count, 1024));
            for (int i = 0; i < count; i++)
            {
                terms.Add(Term());
            }

            return terms;
        }

        internal ShapePath Path()
        {
            switch (Byte())
            {
                case 0:
                    return new PredicatePath(Term());
                case 1:
                    return new SequencePath(Paths());
                case 2:
                    return new AlternativePath(Paths());
                case 3:
                    UnaryPathKind kind = (UnaryPathKind)Byte();
                    return kind <= UnaryPathKind.ZeroOrOne ? new UnaryPath(kind, Path()) : throw new InvalidDataException("A path of an unknown kind.");
                default:
                    throw new InvalidDataException("A path of an unknown kind.");
            }
        }

        private ImmutableArray<ShapePath> Paths()
        {
            int count = Count();
            ImmutableArray<ShapePath>.Builder paths = ImmutableArray.CreateBuilder<ShapePath>(Math.Min(count, 1024));
            for (int i = 0; i < count; i++)
            {
                paths.Add(Path());
            }

            return paths.ToImmutable();
        }
    }
}
