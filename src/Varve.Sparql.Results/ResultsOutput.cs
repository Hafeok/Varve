// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Sparql.Results;

/// <summary>
/// Where a writer's bytes go: the caller's buffer writer, written as the
/// calls are made, or a pooled buffer drained into a stream on flush.
/// </summary>
internal sealed class ResultsOutput : IDisposable
{
    private const int InitialPooled = 16 * 1024;

    private readonly IBufferWriter<byte>? _writer;
    private readonly Stream? _stream;
    private byte[]? _pooled;

    internal ResultsOutput(IBufferWriter<byte> writer) => _writer = writer;

    internal ResultsOutput(Stream stream)
    {
        _stream = stream;
        _pooled = ArrayPool<byte>.Shared.Rent(InitialPooled);
    }

    /// <summary>Bytes buffered for the stream and not yet written to it.</summary>
    internal int Pending { get; private set; }

    [HotPath]
    internal void Write(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        Span<byte> target = GetSpan(bytes.Length);
        bytes.CopyTo(target);
        Advance(bytes.Length);
    }

    [HotPath]
    internal void Write(byte value)
    {
        GetSpan(1)[0] = value;
        Advance(1);
    }

    internal void Flush()
    {
        if (_stream is null || Pending == 0)
        {
            return;
        }

        _stream.Write(_pooled!, 0, Pending);
        Pending = 0;
    }

    internal async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        if (_stream is null || Pending == 0)
        {
            return;
        }

        await _stream.WriteAsync(_pooled.AsMemory(0, Pending), cancellationToken).ConfigureAwait(false);
        Pending = 0;
    }

    public void Dispose()
    {
        if (_pooled is not null)
        {
            ArrayPool<byte>.Shared.Return(_pooled);
            _pooled = null;
        }
    }

    [HotPath]
    private Span<byte> GetSpan(int size)
    {
        if (_writer is not null)
        {
            return _writer.GetSpan(size);
        }

        ObjectDisposedException.ThrowIf(_pooled is null, this);

        if (_pooled.Length - Pending < size)
        {
            Grow(size);
        }

        return _pooled.AsSpan(Pending);
    }

    [HotPath]
    private void Advance(int count)
    {
        if (_writer is not null)
        {
            _writer.Advance(count);
        }
        else
        {
            Pending += count;
        }
    }

    // Growth, not flushing: a stream is written only by Flush and FlushAsync.
    private void Grow(int size)
    {
        byte[] larger = ArrayPool<byte>.Shared.Rent(Math.Max(_pooled!.Length * 2, Pending + size));
        _pooled.AsSpan(0, Pending).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(_pooled);
        _pooled = larger;
    }
}
