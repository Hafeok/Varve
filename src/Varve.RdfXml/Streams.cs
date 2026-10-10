// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.IO;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.RdfXml;

/// <summary>
/// A read-only stream over memory the caller holds, so that
/// <c>XmlReader</c>, which reads streams, can read a span or a sequence
/// without the bytes being copied first.
/// </summary>
internal sealed class ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory) : Stream
{
    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => memory.Length;

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int n = Math.Min(buffer.Length, memory.Length - _position);
        memory.Span.Slice(_position, n).CopyTo(buffer);
        _position += n;
        return n;
    }

    public override void Flush()
    {
    }

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override void SetLength(long value) => throw new NotSupportedException();

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// A read-only stream over a <see cref="ReadOnlySequence{T}"/>, handing the
/// reader each segment's bytes as they lie. The chunk-boundary oracle feeds
/// the parser through this, split at every offset.
/// </summary>
internal sealed class ReadOnlySequenceStream(ReadOnlySequence<byte> sequence) : Stream
{
    private SequencePosition _position = sequence.Start;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => sequence.Length;

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override long Position
    {
        get => sequence.Slice(sequence.Start, _position).Length;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        // One segment at a time, and not the whole request: the point of this
        // stream is that the reader sees the input in the pieces it arrived in.
        while (sequence.TryGet(ref _position, out ReadOnlyMemory<byte> segment, advance: false))
        {
            if (segment.IsEmpty)
            {
                sequence.TryGet(ref _position, out _, advance: true);
                continue;
            }

            int n = Math.Min(buffer.Length, segment.Length);
            segment.Span[..n].CopyTo(buffer);
            _position = sequence.GetPosition(n, _position);
            return n;
        }

        return 0;
    }

    public override void Flush()
    {
    }

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override void SetLength(long value) => throw new NotSupportedException();

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// A write-only stream over the caller's <see cref="IBufferWriter{T}"/>, so
/// that <c>XmlWriter</c>, which writes streams, writes into the sink every
/// other Varve writer takes.
/// </summary>
internal sealed class BufferWriterStream(IBufferWriter<byte> output) : Stream
{
    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override long Length => throw new NotSupportedException();

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        buffer.CopyTo(output.GetSpan(buffer.Length));
        output.Advance(buffer.Length);
    }

    public override void Flush()
    {
    }

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    [DesignDecision(typeof(RdfXmlOverSystemXml.StreamAdaptersAreOneWay), Scope = ExceptionScope.Compatibility)]
    public override void SetLength(long value) => throw new NotSupportedException();
}
