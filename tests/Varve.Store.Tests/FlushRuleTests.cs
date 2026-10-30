// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// The flush before a derived file's rename is kept wherever a reader cannot
/// verify (ADR 0213 point 8). A store with a reader announced is written
/// under the power-loss model: a file renamed into place without a flush is
/// torn when the power is cut. With a reader of format 5 announced, a
/// preview.15 follower, the set's shared blobs are written in version 4,
/// which that reader trusts: every one is flushed, and nothing it reads is
/// torn. With every reader at 6, no checked file is flushed, and each torn
/// one is a miss: the answers after the cut are the answers before it.
/// </summary>
public sealed class FlushRuleTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public async Task a_derived_file_skips_its_flush_only_when_every_reader_verifies_it(int maxDerived)
    {
        await using TemporaryDirectory directory = new(flushes: false);
        TearingFileSystem files = new();
        List<string> expected;

        await using (Announced storage = new(FileStorage.Open(files, directory.Directory, TemporaryDirectory.Options), maxDerived))
        await using (Dataset dataset = await T.OpenOrCreate(storage, MidReadDamageTests.Options()))
        {
            await MidReadDamageTests.WriteHistoryAsync(dataset);
            expected = await ViewsAsync(dataset);
        }

        // Every derived file published, by whether it was flushed first.
        List<string> flushed = [];
        List<string> unflushed = [];

        foreach ((string path, bool wasFlushed) in files.Published)
        {
            string name = Path.GetRelativePath(Path.Combine(directory.Path, "derived"), path).Replace(Path.DirectorySeparatorChar, '/');

            // The backend's own small files are not derived blobs, and are not flushed (FileStorage).
            if (!name.StartsWith("..", StringComparison.Ordinal) && name is not ("LOCK.owner" or ".gitignore") && File.Exists(path))
            {
                (wasFlushed ? flushed : unflushed).Add(name);
            }
        }

        // What decides the flush is the file's own header: version 6 of a
        // checked kind is not flushed, and everything else is.
        foreach (string name in unflushed)
        {
            Assert.True(VerifiesItself(Path.Combine(directory.Path, "derived", name)), name + " was published without a flush, and does not verify itself");
        }

        foreach (string name in flushed)
        {
            Assert.False(VerifiesItself(Path.Combine(directory.Path, "derived", name)), name + " verifies itself, and was flushed");
        }

        // The set's shared blobs: version 4 under a reader of 5, and flushed;
        // version 6 under readers of 6, and not.
        List<string> shared = [.. flushed.FindAll(IsShared), .. unflushed.FindAll(IsShared)];
        Assert.NotEmpty(shared);
        Assert.All(shared, name => Assert.Equal(maxDerived < DerivedFormat.CheckedVersion, flushed.Contains(name)));
        Assert.NotEmpty(unflushed);

        // The power is cut: every file renamed without a flush is torn, its
        // second half zeroed as a page cache never written back leaves it.
        foreach (string name in unflushed)
        {
            string path = Path.Combine(directory.Path, "derived", name);
            byte[] bytes = File.ReadAllBytes(path);
            Array.Clear(bytes, bytes.Length / 2, bytes.Length - (bytes.Length / 2));
            File.WriteAllBytes(path, bytes);
        }

        await using (Announced storage = new(FileStorage.Open(new TearingFileSystem(), directory.Directory, TemporaryDirectory.Options), maxDerived))
        await using (Dataset dataset = await Dataset.OpenAsync(storage, MidReadDamageTests.Options(), T.Ct))
        {
            Assert.Equal(expected, await ViewsAsync(dataset));
        }

        TestContext.Current.TestOutputHelper?.WriteLine("maxDerived " + maxDerived + ": " + flushed.Count + " flushed, " + unflushed.Count + " torn");
    }

    private static bool IsShared(string name) =>
        name.EndsWith("/commits", StringComparison.Ordinal) || name.EndsWith("/terms", StringComparison.Ordinal);

    private static bool VerifiesItself(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return bytes.Length >= DerivedFormat.HeaderLength && DerivedFormat.VerifiesItself(bytes.AsSpan(bytes.Length - DerivedFormat.HeaderLength));
    }

    // Every position's quads, and the head's.
    private static async Task<List<string>> ViewsAsync(Dataset dataset)
    {
        List<string> views = [];

        for (long q = 1; q <= dataset.Head.Value; q++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(q), T.Ct);
            views.Add(q + ": " + string.Join(" ", T.Terms(view)));
        }

        views.Add("head: " + string.Join(" ", T.Terms(dataset.Pin())));
        return views;
    }

    /// <summary>A file storage whose reader records hold one other reader, of <see cref="MaxDerived"/> as given.</summary>
    internal sealed class Announced(FileStorage storage, int maxDerived) : IStorage, IExtentReaders, IAsyncDisposable
    {
        /// <summary>The newest derived format the other reader reads: it restarts on another build when this changes.</summary>
        internal int MaxDerived { get; set; } = maxDerived;

        public ISegmentStore Log => storage.Log;

        public IDerivedStore Derived => storage.Derived;

        public bool DeletesExtents => true;

        public ValueTask AnnounceAsync(CatalogVersion oldest, CatalogVersion newest, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<ReaderAnnouncement>> ReadAnnouncementsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ReaderAnnouncement>>(
                [new ReaderAnnouncement(new ReaderId("other"), CatalogVersion.Newest, CatalogVersion.Newest) { MaxDerived = new DerivedVersion(MaxDerived) }]);

        public ValueTask<IReadOnlyDictionary<string, long>> ExtentBytesAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>());

        public ValueTask DisposeAsync() => storage.DisposeAsync();
    }

    /// <summary>The disk, noting of each file renamed into place whether it was flushed after its last write.</summary>
    private sealed class TearingFileSystem : IFileSystem
    {
        private readonly DiskFileSystem _disk = new();
        private readonly ConcurrentDictionary<string, bool> _flushed = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> _published = new(StringComparer.Ordinal);

        internal IEnumerable<(string Path, bool Flushed)> Published
        {
            get
            {
                foreach (KeyValuePair<string, bool> entry in _published)
                {
                    yield return (entry.Key, entry.Value);
                }
            }
        }

        public bool DirectoryExists(string path) => _disk.DirectoryExists(path);

        public void CreateDirectory(string path) => _disk.CreateDirectory(path);

        public bool FileExists(string path) => _disk.FileExists(path);

        public IReadOnlyList<string> Files(string directory) => _disk.Files(directory);

        public IFileHandle Open(string path, FileOpen mode) => new Handle(_disk.Open(path, mode), path, _flushed);

        public void Move(string from, string to)
        {
            _disk.Move(from, to);
            _published[to] = _flushed.TryGetValue(from, out bool flushed) && flushed;
        }

        public void Delete(string path) => _disk.Delete(path);

        public bool IsReadOnly(string path) => _disk.IsReadOnly(path);

        public void MakeReadOnly(string path) => _disk.MakeReadOnly(path);

        public IDisposable? TryLock(string path) => _disk.TryLock(path);

        private sealed class Handle(IFileHandle file, string path, ConcurrentDictionary<string, bool> flushed) : IFileHandle
        {
            public long Length => file.Length;

            public ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
            {
                flushed[path] = false;
                return file.WriteAsync(offset, bytes, cancellationToken);
            }

            public int Read(long offset, Span<byte> destination) => file.Read(offset, destination);

            public void Flush()
            {
                file.Flush();
                flushed[path] = true;
            }

            public void Dispose() => file.Dispose();
        }
    }
}
