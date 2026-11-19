// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Microsoft.JSInterop;

namespace Varve.WebUi.Explorer.Pages;

/// <summary>
/// A download on its way to a file in the browser (ADR 0249 point 5): each
/// chunk the client copies is handed to <c>webui.js</c> as it arrives, so the
/// .NET heap never holds the body whole; the browser saves the chunks as one
/// file when the copy ends.
/// </summary>
[Contract(typeof(TheDataExplorersReads.ReadAheadToACeilingThenClose), Role = "a download copied as it arrives, never held whole")]
internal sealed class BrowserFile(IJSRuntime js, int id, Action<long> progress) : Stream
{
    private long _written;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _written;

    public override long Position { get => _written; set => throw new NotSupportedException(); }

    /// <summary>Saves what was written as <paramref name="name"/>, of <paramref name="mediaType"/>.</summary>
    public ValueTask SaveAsync(string name, string mediaType) => js.InvokeVoidAsync("varveWebUi.downloadSave", id, name, mediaType);

    /// <summary>Drops what was written: the download failed.</summary>
    public ValueTask DropAsync() => js.InvokeVoidAsync("varveWebUi.downloadDrop", id);

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        await js.InvokeVoidAsync("varveWebUi.downloadAppend", cancellationToken, id, buffer.ToArray()).ConfigureAwait(false);
        _written += buffer.Length;
        progress(_written);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    // Copies in the browser are asynchronous; a synchronous write goes through
    // the in-process runtime WebAssembly gives.
    public override void Write(byte[] buffer, int offset, int count)
    {
        ((IJSInProcessRuntime)js).InvokeVoid("varveWebUi.downloadAppend", id, buffer.AsSpan(offset, count).ToArray());
        _written += count;
        progress(_written);
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
