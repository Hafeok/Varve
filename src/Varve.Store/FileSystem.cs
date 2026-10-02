// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Microsoft.Win32.SafeHandles;

namespace Varve.Store;

/// <summary>How a file is opened.</summary>
internal enum FileOpen
{
    /// <summary>Read only; the file must exist.</summary>
    Read,

    /// <summary>Read and write; the file must not exist yet.</summary>
    CreateNew,

    /// <summary>Read and write; the file must exist.</summary>
    Write,
}

/// <summary>
/// The file system as the file backend uses it: the one process boundary in
/// the store, and so the one place a test double stands (docs/testing.md,
/// Detroit-style). The fault-injection suite replaces it with a file system
/// that crashes, tears and reorders writes (milestone 6a).
/// </summary>
internal interface IFileSystem
{
    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    bool FileExists(string path);

    /// <summary>Every file under a directory, recursively, as full paths.</summary>
    IReadOnlyList<string> Files(string directory);

    IFileHandle Open(string path, FileOpen mode);

    /// <summary>Renames a file, replacing any file at the destination.</summary>
    void Move(string from, string to);

    void Delete(string path);

    bool IsReadOnly(string path);

    void MakeReadOnly(string path);

    /// <summary>Takes an exclusive lock on a file, or returns null when another holder has it (ADR 0075).</summary>
    IDisposable? TryLock(string path);
}

/// <summary>An open file, written and read at offsets.</summary>
internal interface IFileHandle : IDisposable
{
    long Length { get; }

    ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    int Read(long offset, Span<byte> destination);

    /// <summary>Makes what was written durable: <c>RandomAccess.FlushToDisk</c> (ADR 0073).</summary>
    void Flush();
}

/// <summary>The machine's file system, through <see cref="RandomAccess"/>.</summary>
internal sealed class DiskFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public bool FileExists(string path) => File.Exists(path);

    public IReadOnlyList<string> Files(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories) : [];

    public IFileHandle Open(string path, FileOpen mode) => mode switch
    {
        FileOpen.Read => new DiskFile(File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)),
        FileOpen.CreateNew => new DiskFile(File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete)),
        _ => new DiskFile(File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete)),
    };

    public void Move(string from, string to) => File.Move(from, to, overwrite: true);

    public void Delete(string path) => File.Delete(path);

    public bool IsReadOnly(string path) => (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;

    public void MakeReadOnly(string path) => File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

    public IDisposable? TryLock(string path)
    {
        try
        {
            // FileShare.None is flock(LOCK_EX) on Unix and a share mode on
            // Windows; the operating system releases it when the process ends.
            return File.OpenHandle(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class DiskFile(SafeFileHandle handle) : IFileHandle
    {
        public long Length => RandomAccess.GetLength(handle);

        public ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
            RandomAccess.WriteAsync(handle, bytes, offset, cancellationToken);

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        [DesignDecision(typeof(SynchronousReadsOverAsynchronousStorage.ReadPathByBenchmark), Scope = ExceptionScope.HotPath)]
        public int Read(long offset, Span<byte> destination)
        {
            int total = 0;

            while (total < destination.Length)
            {
                int read = RandomAccess.Read(handle, destination[total..], offset + total);

                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        public void Flush() => RandomAccess.FlushToDisk(handle);

        public void Dispose() => handle.Dispose();
    }
}
