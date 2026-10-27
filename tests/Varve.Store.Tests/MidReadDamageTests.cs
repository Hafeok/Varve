// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.Tests.Objects;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests;

/// <summary>
/// Damage found in the middle of a read is a miss, never an error and never
/// a different answer (ADR 0213, #146): a byte damaged in every block a scan
/// reads, on files and on objects, once the dataset is open and the view
/// pinned. The run it belongs to is rebuilt from the log for its window and
/// the read carries on where it was: the streamed result equals the
/// undamaged one byte for byte, with no quad twice and none missing. A
/// second damage, in another blob, during the same read degrades the same
/// way, and so does damage to every blob at once, down to the log.
/// </summary>
public sealed class MidReadDamageTests
{
    private const int Commits = 30;

    [Fact]
    public async Task on_files_damage_at_any_block_a_read_reads_is_answered_from_the_log()
    {
        await using TemporaryDirectory directory = new(flushes: false);
        Damage damage = new();
        IFileSystem files = new DamagingFileSystem(new UnflushedFileSystem(), damage);

        await using (FileStorage storage = FileStorage.Open(files, directory.Directory, TemporaryDirectory.Options))
        await using (Dataset dataset = await T.OpenOrCreate(storage, Options()))
        {
            await WriteHistoryAsync(dataset);
        }

        await SweepAsync("files", damage, async () =>
        {
            FileStorage storage = FileStorage.Open(files, directory.Directory, TemporaryDirectory.Options);
            Dataset dataset = await Dataset.OpenAsync(storage, Options(), T.Ct);
            return (dataset, storage);
        });
    }

    [Fact]
    [Trait(ObjectTraits.Name, ObjectTraits.Objects)]
    public async Task on_objects_damage_at_any_block_a_read_reads_is_answered_from_the_log()
    {
        Damage damage = new();
        await using ObjectHarness objects = new(new MemoryObjectStore());
        DamagingObjectStore store = new(objects.Store, damage);

        await using (Backend writer = await Backend.OpenAsync(Through(objects.Options(), store), T.Ct))
        await using (Dataset dataset = await Dataset.CreateAsync(writer, T.Id, Options(), T.Ct))
        {
            await WriteHistoryAsync(dataset);
        }

        // A reader whose cache holds nothing fetches every page it reads from
        // the store as it reads it, so the damage is in the store's copy, met
        // in the middle of the read.
        await SweepAsync("objects", damage, async () =>
        {
            Backend storage = await Backend.OpenAsync(Through(objects.Options(ObjectStorageAccess.ReadOnly, cacheBytes: 1), store), T.Ct);
            Dataset dataset = await Dataset.OpenAsync(storage, Options(), T.Ct);
            return (dataset, storage);
        }, spread: 61, healedAtLeast: 0.4);
    }

    [Fact]
    public async Task a_set_found_damaged_is_written_again_from_the_log_by_the_writer()
    {
        await using TemporaryDirectory directory = new(flushes: false);
        Damage damage = new();
        IFileSystem files = new DamagingFileSystem(new UnflushedFileSystem(), damage);
        List<string> expected;
        string damaged;

        await using (FileStorage storage = FileStorage.Open(files, directory.Directory, TemporaryDirectory.Options))
        await using (Dataset dataset = await T.OpenOrCreate(storage, Options()))
        {
            await WriteHistoryAsync(dataset);
            expected = await AsOfAsync(dataset);
            damaged = dataset.SetNamesForTests()[1];
        }

        // Every 61st byte of every blob of the second set, from the open on.
        foreach (string file in System.IO.Directory.GetFiles(System.IO.Path.Combine(directory.Path, "derived", damaged.TrimEnd('/'))))
        {
            for (long at = 0; at < new System.IO.FileInfo(file).Length; at += 61)
            {
                damage.Arm(damaged + System.IO.Path.GetFileName(file), at);
            }
        }

        await using (FileStorage storage = FileStorage.Open(files, directory.Directory, TemporaryDirectory.Options))
        await using (Dataset dataset = await Dataset.OpenAsync(storage, Options(), T.Ct))
        {
            Assert.Contains(damaged, dataset.SetNamesForTests());
            damage.Live(record: false);
            Assert.Equal(expected, await AsOfAsync(dataset));
            Assert.Contains(dataset.Damaged, name => name.StartsWith(damaged, StringComparison.Ordinal));

            // The writer's next round writes the set again, under a new name.
            await dataset.MaintainAsync(T.Ct);
            Assert.DoesNotContain(damaged, dataset.SetNamesForTests());
            Assert.Equal(expected, await AsOfAsync(dataset));
        }

        // Opened again with the damage still there, nothing reads it.
        await using (FileStorage storage = FileStorage.Open(files, directory.Directory, TemporaryDirectory.Options))
        await using (Dataset dataset = await Dataset.OpenAsync(storage, Options(), T.Ct))
        {
            Assert.Equal(expected, await AsOfAsync(dataset));
            Assert.Empty(dataset.Damaged);
        }

        damage.StopRecording();
    }

    // Every position's quads, by scan, from the views inside and at the ends of the sets.
    private static async Task<List<string>> AsOfAsync(Dataset dataset)
    {
        List<string> views = [];

        for (long q = 1; q <= dataset.Head.Value; q++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(q), T.Ct);
            views.Add(q + ": " + string.Join(" ", T.Terms(view)));
        }

        return views;
    }

    internal static DatasetOptions Options() =>
        new() { Clock = ManualClock.Epoch(), CommitCache = T.CommitCache, MemtableLimit = new QuadCount(8), Maintenance = MaintenanceMode.Off, ExtentMerges = ExtentMergePolicy.Off };

    // The harness's options, through the damaging store.
    private static ObjectStorageOptions Through(ObjectStorageOptions options, IObjectStore store) => new()
    {
        Store = store,
        Prefix = options.Prefix,
        Cache = options.Cache,
        Access = options.Access,
        Instance = options.Instance,
        Clock = options.Clock,
        ProbeRandomness = options.ProbeRandomness,
        CacheBytes = options.CacheBytes,
        Reader = options.Reader,
        Announcing = options.Announcing,
    };

    // Retractions and assertions over a small vocabulary; two checkpoints,
    // which are extent sets, and runs written by maintenance above them.
    internal static async Task WriteHistoryAsync(Dataset dataset)
    {
        Random random = new(146);

        for (int c = 1; c <= Commits; c++)
        {
            CommitRequest request = new();

            for (int i = 0; i < 6; i++)
            {
                int s = random.Next(24);
                RdfTerm o = T.Literal("ø" + (s % 5).ToString(CultureInfo.InvariantCulture) + " " + (c % 3).ToString(CultureInfo.InvariantCulture));

                if (random.Next(3) == 0)
                {
                    request.Retract(T.Iri("s" + s), T.Iri("p" + (s % 3)), o);
                }
                else
                {
                    request.Assert(T.Iri("s" + s), T.Iri("p" + (s % 3)), o);
                }
            }

            await dataset.CommitAsync(request, T.Ct);
            await dataset.MaintainAsync(T.Ct);

            if (c is 10 or 20)
            {
                await dataset.CheckpointAsync(dataset.Head, T.Ct);
            }
        }
    }

    // Every read of derived/ an undamaged read makes, then the same read
    // with each of them damaged, with two damaged in two blobs, and with all
    // of them damaged at once.
    // A read of a file is a block; a fetch from an object store is a page,
    // which holds many blocks, so on objects a byte in every 61 of each page
    // fetched is damaged in turn.
    private static async Task SweepAsync(string backend, Damage damage, Func<Task<(Dataset Dataset, IAsyncDisposable Storage)>> open, int spread = 0, double healedAtLeast = 1)
    {
        damage.Clear();
        (string expected, List<(string Name, long Offset, int Length)> recorded, int undamaged) = await ReadAsync(open, damage, record: true);
        Assert.Equal(0, undamaged);
        List<(string Name, long Offset)> reads = spread == 0
            ? [.. recorded.Select(r => (r.Name, r.Offset))]
            : [.. recorded.SelectMany(r => Enumerable.Range(0, (r.Length + spread - 1) / spread).Select(i => (r.Name, r.Offset + ((long)i * spread))))];
        Assert.NotEmpty(reads);
        Assert.True(reads.Select(r => r.Name).Distinct().Count() >= 4, backend + ": the read reads too few blobs to say anything: " + string.Join(", ", reads.Select(r => r.Name).Distinct()));

        List<(string Name, long Offset)[]> cases = [.. reads.Select(r => new[] { r })];
        Random pairs = new(213);

        for (int i = 0; i < reads.Count; i++)
        {
            (string Name, long Offset) other = reads[pairs.Next(reads.Count)];

            if (other.Name != reads[i].Name)
            {
                cases.Add([reads[i], other]);
            }
        }

        cases.Add([.. reads]);

        // A pull request checks every fourth case, from where a seed says;
        // the nightly run checks every one (docs/testing.md section 8).
        Sweep sweep = Depth.SweepOf("MIDREAD_DAMAGE", 4);
        List<string> wrong = [];
        int checkedCases = 0;
        int healedCases = 0;

        // The case that damages everything is always checked.
        IEnumerable<int> chosen = Enumerable.Range(0, cases.Count).Where(i => i % sweep.Stride == sweep.First - 1 || i == cases.Count - 1);

        foreach (int i in chosen)
        {
            damage.Clear();

            foreach ((string name, long offset) in cases[i])
            {
                damage.Arm(name, offset);
            }

            checkedCases++;
            string how = string.Join(" and ", cases[i].Take(3).Select(c => c.Name + " at " + c.Offset)) + (cases[i].Length > 3 ? " and " + (cases[i].Length - 3) + " more" : string.Empty);

            try
            {
                (string served, _, int healed) = await ReadAsync(open, damage, record: false);
                healedCases += healed > 0 ? 1 : 0;

                if (served != expected)
                {
                    string[] a = expected.Split('\n');
                    string[] b = served.Split('\n');
                    int d = 0;

                    while (d < Math.Min(a.Length, b.Length) && a[d] == b[d])
                    {
                        d++;
                    }

                    wrong.Add(how + ": served a different answer (first difference, line " + d + ":\n    expected " + (d < a.Length ? a[d] : "<end>") + "\n    served   " + (d < b.Length ? b[d] : "<end>") + ")");
                }
            }
            catch (Exception error) when (error is not Xunit.Sdk.XunitException)
            {
                wrong.Add(how + ": " + error.GetType().Name + ": " + error.Message);
            }
        }

        damage.Clear();
        TestContext.Current.TestOutputHelper?.WriteLine(sweep.Replay + ": " + backend + ": " + checkedCases + " of " + cases.Count + " damaged reads over " + reads.Count + " places in " + recorded.Count + " reads of " + reads.Select(r => r.Name).Distinct().Count() + " blobs; " + healedCases + " met their damage and were answered from the log");
        Assert.True(wrong.Count == 0, sweep.Replay + " replays this. " + backend + ": " + wrong.Count + " of " + checkedCases + " damaged reads were not a miss:\n  " + string.Join("\n  ", wrong.Take(40)));

        // The damage was met: a byte at a block's start always is. On objects
        // a byte of a blob's header or directory, read and verified once at
        // open, is not read again; in these small blobs that is about half
        // of every page.
        Assert.True(healedCases >= healedAtLeast * checkedCases, backend + ": only " + healedCases + " of " + checkedCases + " damaged reads met their damage");
    }

    // The head, read as a stream in the order each pattern yields, every
    // term rendered, and every quad it holds looked up; and the same of the
    // view at each checkpoint and inside each set. The damage armed is applied only once the
    // dataset is open and the views pinned, so it is met in the middle.
    private static async Task<(string Served, List<(string Name, long Offset, int Length)> Reads, int Healed)> ReadAsync(
        Func<Task<(Dataset Dataset, IAsyncDisposable Storage)>> open, Damage damage, bool record)
    {
        (Dataset dataset, IAsyncDisposable storage) = await open();
        List<string> served = [];

        try
        {
            using DatasetView head = dataset.Pin();
            using DatasetView ten = await dataset.AsOfAsync(new Position(10), T.Ct);
            using DatasetView twenty = await dataset.AsOfAsync(new Position(20), T.Ct);

            // Inside a set's window: the set is sought, its columns read too (ADR 0177).
            using DatasetView five = await dataset.AsOfAsync(new Position(5), T.Ct);
            using DatasetView fifteen = await dataset.AsOfAsync(new Position(15), T.Ct);
            damage.Live(record);

            foreach ((string label, DatasetView view) in new[] { ("head", head), ("10", ten), ("20", twenty), ("5", five), ("15", fifteen) })
            {
                Stream(served, label + " all", view, TermHandle.None, TermHandle.None);

                for (int p = 0; p < 3; p++)
                {
                    TermHandle predicate = view.TryInternalise(T.Iri("p" + p), out TermHandle h) ? h : TermHandle.None;

                    if (!predicate.IsNone)
                    {
                        Stream(served, label + " p" + p, view, TermHandle.None, predicate);
                    }
                }

                for (int s = 0; s < 24; s += 5)
                {
                    TermHandle subject = view.TryInternalise(T.Iri("s" + s), out TermHandle h) ? h : TermHandle.None;

                    if (!subject.IsNone)
                    {
                        Stream(served, label + " s" + s, view, subject, TermHandle.None);
                    }
                }
            }

            return (string.Join("\n", served), damage.StopRecording(), dataset.Damaged.Count);
        }
        finally
        {
            damage.StopRecording();
            damage.Disarm();
            await dataset.DisposeAsync();
            await storage.DisposeAsync();
        }
    }

    private static void Stream(List<string> served, string label, DatasetView view, TermHandle subject, TermHandle predicate)
    {
        List<string> quads = [];
        using IQuadCursor cursor = view.Match(subject, predicate, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;
            quads.Add(T.Render(view, quad) + (view.Contains(in quad) ? string.Empty : " NOT HELD"));
        }

        served.Add(label + ": " + view.Estimate(subject, predicate, TermHandle.None, GraphPattern.Any).Count.Value + ": " + string.Join(" | ", quads));
    }

    /// <summary>
    /// The damage a test arms: bytes of named derived blobs flipped as they
    /// are read, once <see cref="Live"/> says so, and the reads recorded.
    /// </summary>
    internal sealed class Damage
    {
        private readonly Lock _lock = new();
        private readonly List<(string Name, long Offset)> _armed = [];
        private readonly List<(string Prefix, long Offset)> _armedUnder = [];
        private readonly List<(string Name, long Offset, int Length)> _reads = [];
        private readonly HashSet<(string, long)> _seen = [];
        private bool _live;
        private bool _recording;

        internal void Arm(string name, long offset)
        {
            lock (_lock)
            {
                _armed.Add((name, offset));
            }
        }

        /// <summary>Damages the byte at <paramref name="offset"/> of every blob under <paramref name="prefix"/>, such as a bulk load's spills.</summary>
        internal void ArmUnder(string prefix, long offset)
        {
            lock (_lock)
            {
                _armedUnder.Add((prefix, offset));
            }
        }

        internal void Clear()
        {
            lock (_lock)
            {
                _armedUnder.Clear();
                _armed.Clear();
                _reads.Clear();
                _seen.Clear();
            }
        }

        internal void Disarm()
        {
            lock (_lock)
            {
                _armedUnder.Clear();
                _armed.Clear();
            }
        }

        internal void Live(bool record)
        {
            lock (_lock)
            {
                _live = true;
                _recording = record;
            }
        }

        internal List<(string Name, long Offset, int Length)> StopRecording()
        {
            lock (_lock)
            {
                _live = false;
                _recording = false;
                return [.. _reads];
            }
        }

        // A read of a derived blob: recorded, and each armed byte in its
        // range flipped. Reads of the log are never touched.
        internal void Apply(string name, long offset, Span<byte> bytes)
        {
            int derived = name.IndexOf("derived/", StringComparison.Ordinal);

            if (derived < 0 || bytes.IsEmpty)
            {
                return;
            }

            string key = name[(derived + "derived/".Length)..];

            lock (_lock)
            {
                if (!_live)
                {
                    return;
                }

                if (_recording && _seen.Add((key, offset)))
                {
                    _reads.Add((key, offset, bytes.Length));
                }

                foreach ((string armed, long at) in _armed)
                {
                    if (armed == key && at >= offset && at < offset + bytes.Length)
                    {
                        bytes[(int)(at - offset)] ^= 0xFF;
                    }
                }

                foreach ((string prefix, long at) in _armedUnder)
                {
                    if (key.StartsWith(prefix, StringComparison.Ordinal) && at >= offset && at < offset + bytes.Length)
                    {
                        bytes[(int)(at - offset)] ^= 0xFF;
                    }
                }
            }
        }
    }

    internal sealed class DamagingFileSystem(IFileSystem inner, Damage damage) : IFileSystem
    {
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public bool FileExists(string path) => inner.FileExists(path);

        public IReadOnlyList<string> Files(string directory) => inner.Files(directory);

        public IFileHandle Open(string path, FileOpen mode) => new Handle(inner.Open(path, mode), path.Replace('\\', '/'), damage);

        public void Move(string from, string to) => inner.Move(from, to);

        public void Delete(string path) => inner.Delete(path);

        public bool IsReadOnly(string path) => inner.IsReadOnly(path);

        public void MakeReadOnly(string path) => inner.MakeReadOnly(path);

        public IDisposable? TryLock(string path) => inner.TryLock(path);

        private sealed class Handle(IFileHandle file, string path, Damage damage) : IFileHandle
        {
            public long Length => file.Length;

            public ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
                file.WriteAsync(offset, bytes, cancellationToken);

            public int Read(long offset, Span<byte> destination)
            {
                int read = file.Read(offset, destination);
                damage.Apply(path, offset, destination[..read]);
                return read;
            }

            public void Flush() => file.Flush();

            public void Dispose() => file.Dispose();
        }
    }

    private sealed class DamagingObjectStore(IObjectStore inner, Damage damage) : IObjectStore
    {
        public ValueTask<ObjectVersion?> CreateIfAbsentAsync(ObjectName name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
            inner.CreateIfAbsentAsync(name, content, cancellationToken);

        public ValueTask<ObjectVersion?> CreateIfAbsentAsync(ObjectName name, ObjectContent content, CancellationToken cancellationToken) =>
            inner.CreateIfAbsentAsync(name, content, cancellationToken);

        public ValueTask<ObjectVersion?> ReplaceIfUnchangedAsync(ObjectName name, ObjectVersion expected, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
            inner.ReplaceIfUnchangedAsync(name, expected, content, cancellationToken);

        public async ValueTask<ObjectRead?> ReadAsync(ObjectName name, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
        {
            ObjectRead? read = await inner.ReadAsync(name, offset, length, cancellationToken);

            if (read is null)
            {
                return null;
            }

            byte[] bytes = read.Bytes.ToArray();
            damage.Apply(name.Value, offset.Value, bytes);
            return read with { Bytes = bytes };
        }

        public ValueTask<IReadOnlyList<ObjectEntry>> ListAsync(ObjectName under, CancellationToken cancellationToken) => inner.ListAsync(under, cancellationToken);

        public ValueTask<bool> DeleteAsync(ObjectName name, CancellationToken cancellationToken) => inner.DeleteAsync(name, cancellationToken);
    }
}
