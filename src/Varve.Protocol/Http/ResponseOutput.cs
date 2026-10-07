// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Varve.Protocol.Http;

/// <summary>A read reached <c>Limits:ResultSizeCap</c> (ADR 0095).</summary>
internal sealed class ReadLimitExceededException : Exception
{
    public ReadLimitExceededException()
        : base("The read reached the server's result size limit.")
    {
    }

    public ReadLimitExceededException(string message)
        : base(message)
    {
    }

    public ReadLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A response body that counts what it is given against the size cap, and
/// holds the first <see cref="Threshold"/> bytes back from the transport.
/// </summary>
/// <remarks>
/// Holding the start back is what lets a read cut early answer with a clean
/// <c>503</c> problem rather than a truncated document (ADR 0095): until the
/// first hand-over, nothing has reached the client. After it, a cut is a
/// trailer or an aborted connection.
/// </remarks>
internal sealed class ResponseOutput : IBufferWriter<byte>
{
    internal const int Threshold = 32 * 1024;

    private readonly HttpResponse _response;
    private readonly long _cap;
    private readonly ArrayBufferWriter<byte> _held = new(Threshold);

    internal ResponseOutput(HttpResponse response, long cap)
    {
        _response = response;
        _cap = cap;
    }

    /// <summary>Whether any byte has been handed to the transport.</summary>
    internal bool HasStarted { get; private set; }

    /// <summary>Every byte written so far.</summary>
    internal long Total { get; private set; }

    public void Advance(int count)
    {
        _held.Advance(count);
        Total += count;

        if (Total > _cap)
        {
            throw new ReadLimitExceededException();
        }
    }

    public Memory<byte> GetMemory(int sizeHint = 0) => _held.GetMemory(sizeHint);

    public Span<byte> GetSpan(int sizeHint = 0) => _held.GetSpan(sizeHint);

    /// <summary>Hands what is held to the transport when it has reached the threshold.</summary>
    internal ValueTask FlushIfDueAsync(CancellationToken cancellationToken) =>
        _held.WrittenCount >= Threshold ? FlushAsync(cancellationToken) : ValueTask.CompletedTask;

    /// <summary>Hands everything held to the transport and flushes it.</summary>
    internal async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        if (_held.WrittenCount == 0)
        {
            return;
        }

        PipeWriter body = _response.BodyWriter;
        HasStarted = true;
        await body.WriteAsync(_held.WrittenMemory, cancellationToken).ConfigureAwait(false);
        _held.ResetWrittenCount();
    }

    /// <summary>Discards what is held, which only a read that has not started may do.</summary>
    internal void Discard() => _held.ResetWrittenCount();
}
