// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Store.Tests;

/// <summary>
/// The machine's file system with <see cref="IFileHandle.Flush"/> a no-op
/// (ADR 0090): every other call reaches the disk, so the suites on it still
/// meet the platform's sharing, renames, locks and read-only attributes, but
/// not the cost of a flush, which on the Windows runner is milliseconds each.
/// What a flush makes durable is proven where it is the subject: the
/// fault-injection suite, whose simulated file system loses what was not
/// flushed, and the durability job, which flushes the real device.
/// </summary>
internal sealed class UnflushedFileSystem : IFileSystem
{
    private readonly DiskFileSystem _disk = new();

    public bool DirectoryExists(string path) => _disk.DirectoryExists(path);

    public void CreateDirectory(string path) => _disk.CreateDirectory(path);

    public bool FileExists(string path) => _disk.FileExists(path);

    public IReadOnlyList<string> Files(string directory) => _disk.Files(directory);

    public IFileHandle Open(string path, FileOpen mode) => new Unflushed(_disk.Open(path, mode));

    public void Move(string from, string to) => _disk.Move(from, to);

    public void Delete(string path) => _disk.Delete(path);

    public bool IsReadOnly(string path) => _disk.IsReadOnly(path);

    public void MakeReadOnly(string path) => _disk.MakeReadOnly(path);

    public IDisposable? TryLock(string path) => _disk.TryLock(path);

    private sealed class Unflushed(IFileHandle file) : IFileHandle
    {
        public long Length => file.Length;

        public ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
            file.WriteAsync(offset, bytes, cancellationToken);

        public int Read(long offset, Span<byte> destination) => file.Read(offset, destination);

        public void Flush()
        {
        }

        public void Dispose() => file.Dispose();
    }
}
