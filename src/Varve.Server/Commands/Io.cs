// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Server.Commands;

/// <summary>
/// Where a command writes: messages to <see cref="Out"/>, failures to
/// <see cref="Error"/>, data — results, quads, feed records — to
/// <see cref="Data"/>, which is standard output in the process and a buffer
/// in a test.
/// </summary>
internal sealed record Io(TextWriter Out, TextWriter Error, Stream Data);

/// <summary>A buffer writer over a stream: data is gathered in memory and flushed when it is due or at the end.</summary>
internal sealed class StreamOutput(Stream stream) : IBufferWriter<byte>
{
    private const int Threshold = 1 << 16;
    private readonly ArrayBufferWriter<byte> _buffer = new(Threshold);

    public void Advance(int count) => _buffer.Advance(count);

    public Memory<byte> GetMemory(int sizeHint = 0) => _buffer.GetMemory(sizeHint);

    public Span<byte> GetSpan(int sizeHint = 0) => _buffer.GetSpan(sizeHint);

    internal bool IsDue => _buffer.WrittenCount >= Threshold;

    internal async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        if (_buffer.WrittenCount > 0)
        {
            await stream.WriteAsync(_buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
            _buffer.ResetWrittenCount();
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A term as N-Triples text, grown to fit.</summary>
    internal void WriteTerm(RdfTerm term)
    {
        WriteOptions options = new() { Syntax = RdfSyntax.NQuads };
        int size = 256;

        while (true)
        {
            Span<byte> span = _buffer.GetSpan(size);

            if (NQuadsWriter.TryWriteTerm(term, span, out int written, in options))
            {
                _buffer.Advance(written);
                return;
            }

            size *= 4;
        }
    }

    internal void Write(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(_buffer.GetSpan(bytes.Length));
        _buffer.Advance(bytes.Length);
    }
}
