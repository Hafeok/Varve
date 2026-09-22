using System;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;

namespace Varve.Turtle.Tests;

/// <summary>
/// Inputs that arrive in pieces. Both halves of the suite need them and a
/// second copy would be a second set of behaviours to keep in step.
/// </summary>
internal static class Fragments
{
    /// <summary>
    /// <paramref name="bytes"/> as a sequence of <paramref name="segmentSize"/>
    /// -byte segments, so that a construct is cut wherever the size lands.
    /// </summary>
    internal static ReadOnlySequence<byte> Fragmented(byte[] bytes, int segmentSize)
    {
        Segment? first = null;
        Segment? last = null;

        for (int i = 0; i < bytes.Length; i += segmentSize)
        {
            int length = Math.Min(segmentSize, bytes.Length - i);
            ReadOnlyMemory<byte> memory = new(bytes, i, length);
            last = first is null ? first = new Segment(memory, 0) : last!.Append(memory);
        }

        return first is null
            ? ReadOnlySequence<byte>.Empty
            : new ReadOnlySequence<byte>(first, 0, last!, last!.Memory.Length);
    }

    internal sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        internal Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        internal Segment Append(ReadOnlyMemory<byte> memory)
        {
            Segment next = new(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }

    /// <summary>A stream that hands back a few bytes at a time, as a socket would.</summary>
    internal sealed class DripStream : Stream
    {
        private readonly byte[] _bytes;
        private readonly int _chunk;
        private int _at;

        internal DripStream(byte[] bytes, int chunk)
        {
            _bytes = bytes;
            _chunk = chunk;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _bytes.Length;

        public override long Position
        {
            get => _at;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int length = Math.Min(Math.Min(_chunk, buffer.Length), _bytes.Length - _at);
            _bytes.AsSpan(_at, length).CopyTo(buffer);
            _at += length;
            return length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
