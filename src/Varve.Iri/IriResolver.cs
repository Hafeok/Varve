// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Iri;

/// <summary>
/// RFC 3986 §5 reference resolution, strict, applied to IRIs per RFC 3987 §6.5.
/// </summary>
/// <remarks>
/// Not a hot path: N-Triples and N-Quads have no base and never resolve.
/// A scratch buffer is taken from the stack for ordinary lengths and from
/// <see cref="ArrayPool{T}"/> beyond, which is why this file is the one place
/// in the package that is not allocation-free by construction.
/// </remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal static class IriResolver
{
    private const int StackThreshold = 512;

    internal static bool TryResolve(
        ReadOnlySpan<byte> baseIri,
        ReadOnlySpan<byte> reference,
        Span<byte> destination,
        out int written)
    {
        written = 0;

        if (!IriScanner.TryValidate(baseIri, out IriComponents b, out _) || !b.HasScheme)
        {
            return false;
        }

        if (!IriScanner.TryValidate(reference, out IriComponents r, out _))
        {
            return false;
        }

        int scratchLength = baseIri.Length + reference.Length + 2;
        byte[]? rented = scratchLength > StackThreshold ? ArrayPool<byte>.Shared.Rent(scratchLength) : null;

        try
        {
            Span<byte> scratch = rented is null ? stackalloc byte[StackThreshold] : rented;
            return Resolve(baseIri, b, reference, r, scratch, destination, out written);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static bool Resolve(
        ReadOnlySpan<byte> baseIri,
        in IriComponents b,
        ReadOnlySpan<byte> reference,
        in IriComponents r,
        Span<byte> scratch,
        Span<byte> destination,
        out int written)
    {
        Writer writer = new(destination);

        ReadOnlySpan<byte> scheme;
        ReadOnlySpan<byte> authority;
        bool hasAuthority;
        ReadOnlySpan<byte> query;
        bool hasQuery;
        int pathLength;

        if (r.HasScheme)
        {
            // §5.2.2, the reference is absolute. Strict: no scheme-matching
            // special case — RFC 3986 §5.2.2's non-strict mode exists for
            // pre-RFC-2396 parsers and would resolve "http:g" against a base.
            scheme = reference[r.Scheme];
            authority = reference[r.Authority];
            hasAuthority = r.HasAuthority;
            query = reference[r.Query];
            hasQuery = r.HasQuery;
            pathLength = RemoveDotSegments(reference[r.Path], default, scratch);
        }
        else
        {
            scheme = baseIri[b.Scheme];

            if (r.HasAuthority)
            {
                authority = reference[r.Authority];
                hasAuthority = true;
                query = reference[r.Query];
                hasQuery = r.HasQuery;
                pathLength = RemoveDotSegments(reference[r.Path], default, scratch);
            }
            else
            {
                authority = baseIri[b.Authority];
                hasAuthority = b.HasAuthority;
                ReadOnlySpan<byte> referencePath = reference[r.Path];

                if (referencePath.IsEmpty)
                {
                    ReadOnlySpan<byte> basePath = baseIri[b.Path];
                    basePath.CopyTo(scratch);
                    pathLength = basePath.Length;
                    query = r.HasQuery ? reference[r.Query] : baseIri[b.Query];
                    hasQuery = r.HasQuery || b.HasQuery;
                }
                else
                {
                    query = reference[r.Query];
                    hasQuery = r.HasQuery;

                    pathLength = referencePath[0] == (byte)'/'
                        ? RemoveDotSegments(referencePath, default, scratch)
                        : RemoveDotSegments(MergeHead(baseIri, b), referencePath, scratch);
                }
            }
        }

        // §5.3 recomposition.
        writer.Write(scheme);
        writer.WriteByte((byte)':');

        if (hasAuthority)
        {
            writer.WriteByte((byte)'/');
            writer.WriteByte((byte)'/');
            writer.Write(authority);
        }

        writer.Write(scratch[..pathLength]);

        if (hasQuery)
        {
            writer.WriteByte((byte)'?');
            writer.Write(query);
        }

        if (r.HasFragment)
        {
            writer.WriteByte((byte)'#');
            writer.Write(reference[r.Fragment]);
        }

        written = writer.Written;
        return !writer.Overflowed;
    }

    /// <summary>
    /// The part of the base path a relative reference is merged onto (§5.2.3):
    /// everything up to and including the last <c>/</c>, or <c>/</c> when the
    /// base has an authority and an empty path.
    /// </summary>
    private static ReadOnlySpan<byte> MergeHead(ReadOnlySpan<byte> baseIri, in IriComponents b)
    {
        ReadOnlySpan<byte> basePath = baseIri[b.Path];

        if (b.HasAuthority && basePath.IsEmpty)
        {
            return "/"u8;
        }

        int lastSlash = basePath.LastIndexOf((byte)'/');
        return lastSlash < 0 ? default : basePath[..(lastSlash + 1)];
    }

    /// <summary>
    /// RFC 3986 §5.2.4, over the concatenation of two spans so that a merge
    /// needs no intermediate copy.
    /// </summary>
    private static int RemoveDotSegments(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail, Span<byte> output)
    {
        Joined input = new(head, tail);
        int length = 0;
        int i = 0;

        while (i < input.Length)
        {
            if (input.StartsWith(i, "../"u8))
            {
                i += 3;
            }
            else if (input.StartsWith(i, "./"u8))
            {
                i += 2;
            }
            else if (input.StartsWith(i, "/./"u8))
            {
                i += 2;
            }
            else if (input.Equals(i, "/."u8))
            {
                i = input.Length;
                length = AppendByte(output, length, (byte)'/');
            }
            else if (input.StartsWith(i, "/../"u8))
            {
                i += 3;
                length = RemoveLastSegment(output, length);
            }
            else if (input.Equals(i, "/.."u8))
            {
                i = input.Length;
                length = RemoveLastSegment(output, length);
                length = AppendByte(output, length, (byte)'/');
            }
            else if (input.Equals(i, "."u8) || input.Equals(i, ".."u8))
            {
                i = input.Length;
            }
            else
            {
                if (input[i] == (byte)'/')
                {
                    length = AppendByte(output, length, (byte)'/');
                    i++;
                }

                while (i < input.Length && input[i] != (byte)'/')
                {
                    length = AppendByte(output, length, input[i]);
                    i++;
                }
            }
        }

        return length;
    }

    private static int AppendByte(Span<byte> output, int length, byte value)
    {
        if (length < output.Length)
        {
            output[length] = value;
        }

        return length + 1;
    }

    /// <summary>Drops the last segment of the output, including its leading slash.</summary>
    private static int RemoveLastSegment(Span<byte> output, int length)
    {
        int limit = Math.Min(length, output.Length);

        while (limit > 0 && output[limit - 1] != (byte)'/')
        {
            limit--;
        }

        return limit > 0 ? limit - 1 : 0;
    }

    /// <summary>Two spans read as one, so a merge needs no intermediate copy.</summary>
    private readonly ref struct Joined
    {
        private readonly ReadOnlySpan<byte> _head;
        private readonly ReadOnlySpan<byte> _tail;

        internal Joined(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail)
        {
            _head = head;
            _tail = tail;
        }

        internal int Length => _head.Length + _tail.Length;

        internal byte this[int index] =>
            index < _head.Length ? _head[index] : _tail[index - _head.Length];

        internal bool StartsWith(int index, ReadOnlySpan<byte> value)
        {
            if (index + value.Length > Length)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (this[index + i] != value[i])
                {
                    return false;
                }
            }

            return true;
        }

        internal bool Equals(int index, ReadOnlySpan<byte> value) =>
            Length - index == value.Length && StartsWith(index, value);
    }

    /// <summary>
    /// Writes into the caller's buffer, and keeps counting past the end so that
    /// one code path serves both "write it" and "how long would it be".
    /// </summary>
    private ref struct Writer
    {
        private readonly Span<byte> _destination;

        internal Writer(Span<byte> destination)
        {
            _destination = destination;
            Written = 0;
            Overflowed = false;
        }

        internal int Written { get; private set; }

        internal bool Overflowed { get; private set; }

        internal void WriteByte(byte value)
        {
            if (Written < _destination.Length)
            {
                _destination[Written] = value;
            }
            else
            {
                Overflowed = true;
            }

            Written++;
        }

        internal void Write(ReadOnlySpan<byte> value)
        {
            if (Written + value.Length <= _destination.Length)
            {
                value.CopyTo(_destination[Written..]);
            }
            else
            {
                Overflowed = true;
            }

            Written += value.Length;
        }
    }
}
