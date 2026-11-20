// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Store.Tests.Faults;

/// <summary>
/// The simulated file system under the concurrency the store really uses
/// (#214): <c>ExtentSet.WriteAsync</c> writes an extent set's order blobs in
/// parallel, each through its own handle, while other files are created,
/// renamed and deleted. Every operation is atomic under the double's one lock,
/// counted once, and its fault semantics are those of the same operations in
/// some serial order.
/// </summary>
public sealed class SimulatedFileSystemConcurrencyTests
{
    private const int Writers = 8;
    private const int FilesPerWriter = 256;
    private const int WritesPerFile = 8;
    private const int Chunk = 300;

    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "varve-simulated-concurrency"));

    /// <summary>
    /// Parallel writers, each creating, writing, flushing and renaming its
    /// own files, as several derived blobs are written at once. Before #214 a
    /// write resolved its path outside the lock, and enumerating the path
    /// table while another writer created a file threw "Collection was
    /// modified".
    /// </summary>
    [Fact]
    public async Task parallel_writers_to_different_files_each_get_their_own_bytes()
    {
        SimulatedFileSystem files = new();
        files.CreateDirectory(Root);

        Dictionary<string, byte[]> expected = await WriteInParallelAsync(files);

        Assert.False(files.Crashed);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), files.Files(Root));

        foreach ((string path, byte[] bytes) in expected)
        {
            Assert.Equal(bytes, files.Read(path));
        }

        // Each write and each flush is one operation, each create and each
        // rename one more; every one counted once, numbered without a gap.
        int perFile = 1 + WritesPerFile + 1 + 1;
        Assert.Equal(Writers * FilesPerWriter * perFile, files.Operations);

        List<(int Operation, int Length, string Path)> writes = files.Writes;
        Assert.Equal(Writers * FilesPerWriter * WritesPerFile, writes.Count);
        Assert.Equal(writes.Count, writes.Select(w => w.Operation).Distinct().Count());
        Assert.All(writes, w => Assert.EndsWith(".tmp", w.Path, StringComparison.Ordinal));
    }

    /// <summary>
    /// A write that starts while a rename of its file holds the lock names the
    /// file as it is named when the write happens: after the rename. Before
    /// #214 the write resolved its path before taking the lock, read the path
    /// table while the rename held it, and was counted against the old name.
    /// This is the race made deterministic: the rename waits, inside the
    /// injector and so under the lock, until the write has started.
    /// </summary>
    [Fact]
    public async Task a_write_racing_a_rename_names_the_file_as_it_is_when_written()
    {
        SimulatedFileSystem files = new();
        files.CreateDirectory(Root);
        string temporary = Path.Combine(Root, "racing.tmp");
        string final = Path.Combine(Root, "racing.blob");
        using IFileHandle handle = files.Open(temporary, FileOpen.CreateNew);

        using ManualResetEventSlim renaming = new();
        using ManualResetEventSlim writing = new();
        files.Injector = op =>
        {
            if (op.Kind == SimulatedFileSystem.OperationKind.Move)
            {
                renaming.Set();
                writing.Wait(T.Ct);

                // Long enough for a write that does not wait for the lock to
                // have read the path table.
                Thread.Sleep(200);
            }

            return null;
        };

        Task rename = Task.Factory.StartNew(() => files.Move(temporary, final), T.Ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        renaming.Wait(T.Ct);
        writing.Set();
        await handle.WriteAsync(0, new byte[] { 1, 2, 3 }, T.Ct);
        await rename;

        (int _, int length, string path) = Assert.Single(files.Writes);
        Assert.Equal(3, length);
        Assert.Equal(Path.GetFullPath(final), path);
    }

    /// <summary>
    /// A crash injected at one operation number while writers run in
    /// parallel: exactly that operation crashes the process, every later
    /// operation on any thread fails as crashed, and the process-crash image
    /// holds every write numbered before it and none after.
    /// </summary>
    [Fact]
    public async Task a_crash_under_parallel_writers_stops_every_writer_at_one_operation()
    {
        int at = Writers * FilesPerWriter * 3;
        SimulatedFileSystem files = new() { Injector = op => op.Number == at ? 0 : null };
        files.CreateDirectory(Root);

        await Assert.ThrowsAnyAsync<SimulatedCrash>(() => WriteInParallelAsync(files));

        Assert.True(files.Crashed);
        Assert.Equal(at, files.Operations);
        Assert.Throws<SimulatedCrash>(() => files.FileExists(Path.Combine(Root, "any")));

        // The image of a process crash keeps every write before the crash and
        // the crashed write's kept bytes, none: the sum of the counted
        // writes is what the image holds.
        SimulatedFileSystem image = files.Image(CrashKind.Process, new Random(0));
        long counted = files.Writes.Where(w => w.Operation < at).Sum(w => (long)w.Length);
        long held = image.Files(Root).Sum(p => (long)image.Read(p).Length);
        Assert.Equal(counted, held);
    }

    /// <summary>
    /// A power loss after parallel writers each flushed their files, whatever
    /// order the writers' operations took (ADR 0073's model). Cut at the last
    /// flush, which is always between two operations, every file is whole
    /// under its final name or, when the rename after its flush was not yet
    /// durable, under its temporary one. Cut anywhere later, every file found
    /// is whole.
    /// </summary>
    [Fact]
    public async Task a_power_loss_after_parallel_writers_keeps_every_flushed_file()
    {
        SimulatedFileSystem files = new();
        files.CreateDirectory(Root);

        Dictionary<string, byte[]> expected = await WriteInParallelAsync(files);
        SimulatedFileSystem atFlush = files.Image((_, _, _) => -1, metadataToEnd: false);

        foreach ((string path, byte[] bytes) in expected)
        {
            string found = atFlush.FileExists(path) ? path : Path.ChangeExtension(path, ".tmp");
            Assert.True(atFlush.FileExists(found), path + " is in the image under neither name");
            Assert.Equal(bytes, atFlush.Read(found));
        }

        for (int seed = 0; seed < 20; seed++)
        {
            SimulatedFileSystem image = files.Image(CrashKind.PowerLoss, new Random(seed));

            foreach ((string path, byte[] bytes) in expected)
            {
                foreach (string name in new[] { path, Path.ChangeExtension(path, ".tmp") }.Where(image.FileExists))
                {
                    Assert.Equal(bytes, image.Read(name));
                }
            }
        }
    }

    // Each writer creates its files as `.tmp`, writes them in chunks through
    // one handle, flushes, and renames each to its final name, interleaved
    // with the others on the thread pool.
    private static async Task<Dictionary<string, byte[]>> WriteInParallelAsync(SimulatedFileSystem files)
    {
        using Barrier start = new(Writers);
        Task<Dictionary<string, byte[]>>[] writers = new Task<Dictionary<string, byte[]>>[Writers];

        for (int w = 0; w < Writers; w++)
        {
            int writer = w;
            // A thread of its own each, so that the writers run at once even
            // when the pool is busy with every other test class. The double
            // completes every write synchronously, so no await leaves it.
            writers[w] = Task.Factory.StartNew(async () =>
            {
                Dictionary<string, byte[]> written = new(StringComparer.Ordinal);
                start.SignalAndWait(T.Ct);

                for (int f = 0; f < FilesPerWriter; f++)
                {
                    string temporary = Path.Combine(Root, "w" + writer + "-" + f + ".tmp");
                    string final = Path.Combine(Root, "w" + writer + "-" + f + ".blob");
                    byte[] bytes = new byte[WritesPerFile * Chunk];
                    new Random((writer * 1000) + f).NextBytes(bytes);

                    using (IFileHandle handle = files.Open(temporary, FileOpen.CreateNew))
                    {
                        for (int c = 0; c < WritesPerFile; c++)
                        {
                            await handle.WriteAsync(c * Chunk, bytes.AsMemory(c * Chunk, Chunk), T.Ct);
                        }

                        handle.Flush();
                    }

                    files.Move(temporary, final);
                    written[Path.GetFullPath(final)] = bytes;
                }

                return written;
            }, T.Ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        }

        Dictionary<string, byte[]>[] results = await Task.WhenAll(writers);
        return results.SelectMany(r => r).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }
}
