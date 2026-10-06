// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Browser;
using Varve.Store.Browser.Model;
using Varve.Store.Log;
using Varve.Store.Tests;

namespace Varve.Store.BrowserTests;

/// <summary>
/// The browser backends' tests, run in a browser (ADR 0084): the storage
/// contract's cases against each backend this context has, the lease, the
/// seal across a reopen, a dataset with disk runs read through the
/// synchronous blob read, the deterministic script's <c>log/</c> as hashes,
/// and measurements of the synchronous read and the flush.
/// </summary>
/// <remarks>
/// It reports every check rather than stopping at the first failure, and
/// ends with <c>OK</c> only when all passed. <c>eng/browser-tests.cs</c>
/// reads the report and compares the hashes with the desktop's.
/// </remarks>
internal static partial class Program
{
    private const string Root = "varve-tests";

    public static void Main()
    {
    }

    [JSExport]
    internal static async Task<string> Run(string thread)
    {
        Report report = new();
        CancellationToken ct = CancellationToken.None;

        try
        {
            await Helpers.ClearOpfs(Root);
            await Helpers.ClearIndexedDb("varve/" + Root);

            bool opfs = await BrowserStorage.IsOriginPrivateFileSystemAvailableAsync(ct);
            report.Line("context: " + thread + "; " + Helpers.UserAgent());
            report.Line("synchronous access handles: " + (opfs ? "available" : "not available"));
            report.Line("maintenance by default: " + new DatasetOptions { Clock = TimeProvider.System }.Maintenance);
            report.Check("maintenance is off by default in a browser", new DatasetOptions { Clock = TimeProvider.System }.Maintenance == MaintenanceMode.Off);

            await using (BrowserStorage chosen = await BrowserStorage.OpenAsync(new BrowserDatasetName(Root + "/chosen"), Options(), ct))
            {
                BrowserBackend expected = thread == "worker" ? BrowserBackend.OriginPrivateFileSystem : BrowserBackend.IndexedDb;
                report.Check("OpenAsync chooses " + expected + " (chose " + chosen.Backend + ")", chosen.Backend == expected);
            }

            List<BrowserBackend> backends = opfs ? [BrowserBackend.OriginPrivateFileSystem, BrowserBackend.IndexedDb] : [BrowserBackend.IndexedDb];

            if (!opfs)
            {
                await report.Expect<PlatformNotSupportedException>(
                    "the OPFS backend refuses to open where synchronous access handles do not exist",
                    async () => await BrowserStorage.OpenOriginPrivateFileSystemAsync(new BrowserDatasetName(Root + "/refused"), Options(), ct));
            }

            foreach (BrowserBackend backend in backends)
            {
                await ContractAsync(report, backend, ct);
                await LeaseAsync(report, backend, ct);
                await SealSurvivesReopenAsync(report, backend, ct);
                await HeldBlobsAsync(report, backend, ct);
                await DatasetWithDiskRunsAsync(report, backend, ct);
                await MeasureAsync(report, backend, ct);
            }

            if (opfs)
            {
                await DeterminismAsync(report, ct);
            }
        }
        catch (Exception e)
        {
            report.Fail("unexpected " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
        }

        return report.Finish();
    }

    private static BrowserStorageOptions Options() => new() { Clock = DeterministicScript.Clock() };

    private static ValueTask<BrowserStorage> OpenAsync(BrowserBackend backend, string name, CancellationToken ct) =>
        backend == BrowserBackend.OriginPrivateFileSystem
            ? BrowserStorage.OpenOriginPrivateFileSystemAsync(new BrowserDatasetName(Root + "/" + name), Options(), ct)
            : BrowserStorage.OpenIndexedDbAsync(new BrowserDatasetName(Root + "/" + name), Options(), ct);

    // Every dataset a run opens has a name of its own, numbered by the report.
    private static string Fresh(Report report, BrowserBackend backend, string what) =>
        backend + "/" + what + "-" + report.Next().ToString(CultureInfo.InvariantCulture);

    private static async Task ContractAsync(Report report, BrowserBackend backend, CancellationToken ct)
    {
        foreach ((string name, Func<Func<ValueTask<IStorage>>, Durability, CancellationToken, Task> run) in StorageContractCases.All())
        {
            List<BrowserStorage> opened = [];

            async ValueTask<IStorage> CreateAsync()
            {
                BrowserStorage storage = await OpenAsync(backend, Fresh(report, backend, "contract"), ct);
                opened.Add(storage);
                return storage;
            }

            try
            {
                await run(CreateAsync, Durability.Committed, ct);
                report.Pass(backend + ": " + name);
            }
            catch (Exception e)
            {
                report.Fail(backend + ": " + name + ": " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                foreach (BrowserStorage storage in opened)
                {
                    await storage.DisposeAsync();
                }
            }
        }
    }

    private static async Task LeaseAsync(Report report, BrowserBackend backend, CancellationToken ct)
    {
        string name = Fresh(report, backend, "lease");
        BrowserStorage first = await OpenAsync(backend, name, ct);

        try
        {
            await report.Expect<DatasetLeasedException>(
                backend + ": a second opener of one dataset is refused while the first holds it",
                async () => await (await OpenAsync(backend, name, ct)).DisposeAsync());
        }
        finally
        {
            await first.DisposeAsync();
        }

        await using BrowserStorage again = await OpenAsync(backend, name, ct);
        report.Pass(backend + ": the lease is released by DisposeAsync");
    }

    private static async Task SealSurvivesReopenAsync(Report report, BrowserBackend backend, CancellationToken ct)
    {
        string name = Fresh(report, backend, "seal");

        await using (BrowserStorage storage = await OpenAsync(backend, name, ct))
        {
            SegmentId first = await storage.Log.CreateSegmentAsync(ct);
            await storage.Log.AppendAsync(first, new byte[] { 1, 2, 3 }, ct);
            await storage.Log.SealAsync(first, ct);
            SegmentId second = await storage.Log.CreateSegmentAsync(ct);
            await storage.Log.AppendAsync(second, new byte[] { 4 }, ct);
            await storage.Log.FlushAsync(second, ct);
            await storage.Log.SealAsync(second, ct);
        }

        await using (BrowserStorage storage = await OpenAsync(backend, name, ct))
        {
            IReadOnlyList<SegmentInfo> segments = await storage.Log.ListSegmentsAsync(ct);
            report.Check(
                backend + ": segments, lengths and seals survive a reopen (" + string.Join(", ", segments.Select(static s => s.Id.Value + ":" + s.Length.Value + (s.IsSealed ? " sealed" : " open"))) + ")",
                segments.Count == 2 && segments[0] == SegmentInfo.Sealed(new SegmentId(0), new ByteCount(3)) && segments[1] == SegmentInfo.Sealed(new SegmentId(1), new ByteCount(1)));
            ReadOnlyMemory<byte> bytes = await storage.Log.ReadRangeAsync(new SegmentId(0), new ByteOffset(0), new ByteCount(10), ct);
            report.Check(backend + ": a reopened segment reads back", bytes.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
        }
    }

    private static async Task HeldBlobsAsync(Report report, BrowserBackend backend, CancellationToken ct)
    {
        string name = Fresh(report, backend, "held");
        byte[] buffer = new byte[8];

        await using (BrowserStorage storage = await OpenAsync(backend, name, ct))
        {
            await PutAsync(storage, "runs/a", [1, 2, 3], ct);
            await PutAsync(storage, "runs/b", [4, 5], ct);
            using IReadableBlob a = await storage.Derived.OpenAsync(new BlobName("runs/a"), ct);
            using IReadableBlob again = await storage.Derived.OpenAsync(new BlobName("runs/a"), ct);
            using IReadableBlob b = await storage.Derived.OpenAsync(new BlobName("runs/b"), ct);

            report.Check(backend + ": a blob deleted while a reader holds it leaves the listing", await storage.Derived.DeleteAsync(new BlobName("runs/a"), ct)
                && (await storage.Derived.ListAsync(ct)) is [{ Value: "runs/b" }]);
            await PutAsync(storage, "runs/b", [6], ct);
            await PutAsync(storage, "runs/b", [7, 8, 9, 10], ct);
            report.Check(
                backend + ": two readers of one blob, and a reader of a blob replaced twice, keep reading their bytes",
                a.Read(new ByteOffset(1), buffer) == 2 && buffer[0] == 2 && again.Read(new ByteOffset(0), buffer) == 3 && buffer[2] == 3
                && b.Read(new ByteOffset(0), buffer) == 2 && buffer[1] == 5);
        }

        await using (BrowserStorage storage = await OpenAsync(backend, name, ct))
        {
            using IReadableBlob b = await storage.Derived.OpenAsync(new BlobName("runs/b"), ct);
            report.Check(
                backend + ": after a reopen the deleted blob is gone and the replaced one is the newest",
                (await storage.Derived.ListAsync(ct)) is [{ Value: "runs/b" }] && b.Length == new ByteCount(4) && b.Read(new ByteOffset(3), buffer) == 1 && buffer[0] == 10);
        }
    }

    private static async Task PutAsync(BrowserStorage storage, string name, byte[] bytes, CancellationToken ct)
    {
        await using IBlobWriter writer = await storage.Derived.CreateAsync(new BlobName(name), ct);
        await writer.WriteAsync(bytes, ct);
        await writer.PublishAsync(ct);
    }

    private static async Task DatasetWithDiskRunsAsync(Report report, BrowserBackend backend, CancellationToken ct)
    {
        string name = Fresh(report, backend, "dataset");
        DatasetOptions options = new()
        {
            Clock = DeterministicScript.Clock(),
            SegmentBytes = new ByteCount(4096),
            MemtableLimit = new QuadCount(16),
            Maintenance = MaintenanceMode.Off,
        };

        await using (BrowserStorage storage = await OpenAsync(backend, name, ct))
        await using (Dataset dataset = await Dataset.CreateAsync(storage, StorageContractCases.Id, options, ct))
        {
            for (int i = 0; i < 60; i++)
            {
                await dataset.CommitAsync(new CommitRequest().Assert(StorageContractCases.Iri("s" + i), StorageContractCases.Iri("p"), StorageContractCases.Literal("v" + i)), ct);

                if (i % 20 == 19)
                {
                    await dataset.MaintainAsync(ct);
                }
            }

            await dataset.CheckpointAsync(new Position(30), ct);
            await dataset.MaintainAsync(ct);
        }

        await using (BrowserStorage storage = await OpenAsync(backend, name, ct))
        {
            IReadOnlyList<BlobName> blobs = await storage.Derived.ListAsync(ct);
            report.Line(backend + ": derived blobs after maintenance: " + string.Join(", ", blobs));

            await using Dataset reopened = await Dataset.OpenAsync(storage, options, ct);
            using DatasetView head = reopened.Pin();
            using DatasetView at30 = await reopened.AsOfAsync(new Position(30), ct);
            using DatasetView at45 = await reopened.AsOfAsync(new Position(45), ct);
            report.Check(
                backend + ": a dataset with disk runs reopens at its head and reads as of a checkpoint and past it ("
                + reopened.Head + ", " + StorageContractCases.Count(head) + ", " + StorageContractCases.Count(at30) + ", " + StorageContractCases.Count(at45) + ")",
                reopened.Head == new Position(60) && StorageContractCases.Count(head) == 60 && StorageContractCases.Count(at30) == 30 && StorageContractCases.Count(at45) == 45
                && reopened.Checkpoints.Count == 1 && blobs.Count > 1);
        }
    }

    private static async Task MeasureAsync(Report report, BrowserBackend backend, CancellationToken ct)
    {
        const int BlobBytes = 16 << 20;
        const int Block = 4096;
        string name = Fresh(report, backend, "measure");

        await using BrowserStorage storage = await OpenAsync(backend, name, ct);
        byte[] chunk = new byte[1 << 20];

        for (int i = 0; i < chunk.Length; i++)
        {
            chunk[i] = (byte)(i * 31);
        }

        Stopwatch watch = Stopwatch.StartNew();

        await using (IBlobWriter writer = await storage.Derived.CreateAsync(new BlobName("measure"), ct))
        {
            for (int i = 0; i < BlobBytes / chunk.Length; i++)
            {
                await writer.WriteAsync(chunk, ct);
            }

            await writer.PublishAsync(ct);
        }

        double publish = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        using IReadableBlob blob = await storage.Derived.OpenAsync(new BlobName("measure"), ct);
        double open = watch.Elapsed.TotalMilliseconds;
        byte[] buffer = new byte[Block];
        int blocks = BlobBytes / Block;
        long sum = 0;

        watch.Restart();

        for (int i = 0; i < blocks; i++)
        {
            sum += blob.Read(new ByteOffset((long)i * Block), buffer);
        }

        double sequential = watch.Elapsed.TotalMilliseconds;
        Random random = new(84);
        watch.Restart();

        for (int i = 0; i < 10_000; i++)
        {
            sum += blob.Read(new ByteOffset((long)random.Next(blocks) * Block), buffer);
        }

        double scattered = watch.Elapsed.TotalMilliseconds;
        byte[] probe = new byte[100];
        int probed = blob.Read(new ByteOffset((5L << 20) + 12_345), probe);
        report.Check(
            backend + ": a 16 MiB blob reads back whole and in place",
            sum == BlobBytes + (10_000L * Block) && probed == probe.Length && probe.AsSpan().SequenceEqual(chunk.AsSpan(12_345, 100)) && blob.Read(new ByteOffset(BlobBytes), probe) == 0);

        SegmentId segment = await storage.Log.CreateSegmentAsync(ct);
        byte[] record = new byte[256];
        double[] flushes = new double[200];

        for (int i = 0; i < flushes.Length; i++)
        {
            long start = Stopwatch.GetTimestamp();
            await storage.Log.AppendAsync(segment, record, ct);
            await storage.Log.FlushAsync(segment, ct);
            flushes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(flushes);
        report.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"measure {backend}: publish 16 MiB {publish:F1} ms, open {open:F1} ms; 4 KiB read {sequential * 1000 / blocks:F2} us in order ({blocks} blocks), {scattered * 1000 / 10_000:F2} us scattered (10,000); 256-byte append and flush median {flushes[flushes.Length / 2]:F3} ms, 90th {flushes[flushes.Length * 9 / 10]:F3} ms"));
    }

    private static async Task DeterminismAsync(Report report, CancellationToken ct)
    {
        string name = Root + "/determinism";
        BrowserStorage storage = await BrowserStorage.OpenOriginPrivateFileSystemAsync(new BrowserDatasetName(name), new BrowserStorageOptions { Clock = DeterministicScript.Clock() }, ct);
        await DeterministicScript.RunAsync(storage, ct);

        string[] files = (await Helpers.OpfsFiles(name + "/log")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        report.Check("determinism: the script wrote a manifest and more than one segment (" + files.Length + " files)", files.Length > 2);

        foreach (string file in files)
        {
            JSObject bytes = await Helpers.OpfsRead(name + "/log/" + file);
            byte[] copy = new byte[Helpers.ByteLength(bytes)];
            Helpers.CopyOut(bytes, copy);
            bytes.Dispose();
            report.Line("determinism log/" + file + " " + Convert.ToHexStringLower(SHA256.HashData(copy)));
        }
    }

    /// <summary>The report: one line per check, <c>OK</c> at the end only when every check passed.</summary>
    private sealed class Report
    {
        private readonly StringBuilder _text = new();
        private int _passed;
        private int _failed;
        private int _names;

        public int Next() => ++_names;

        public void Line(string line) => _text.Append(line).Append('\n');

        public void Pass(string what)
        {
            _passed++;
            Line("pass " + what);
        }

        public void Fail(string what)
        {
            _failed++;
            Line("FAIL " + what);
        }

        public void Check(string what, bool passed)
        {
            if (passed)
            {
                Pass(what);
            }
            else
            {
                Fail(what);
            }
        }

        public async Task Expect<TException>(string what, Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action();
                Fail(what + ": nothing was thrown");
            }
            catch (TException)
            {
                Pass(what);
            }
            catch (Exception e)
            {
                Fail(what + ": " + e.GetType().Name + ": " + e.Message);
            }
        }

        public string Finish()
        {
            Line(_passed.ToString(CultureInfo.InvariantCulture) + " passed, " + _failed.ToString(CultureInfo.InvariantCulture) + " failed");
            Line(_failed == 0 && _passed > 0 ? "OK" : "FAIL");
            return _text.ToString();
        }
    }

    /// <summary>The test app's own helpers, registered by <c>run.js</c>.</summary>
    private static partial class Helpers
    {
        [JSImport("clearOpfs", "varve-browser-tests")]
        public static partial Task ClearOpfs(string path);

        [JSImport("clearIndexedDb", "varve-browser-tests")]
        public static partial Task ClearIndexedDb(string prefix);

        [JSImport("opfsFiles", "varve-browser-tests")]
        public static partial Task<string> OpfsFiles(string path);

        [JSImport("opfsRead", "varve-browser-tests")]
        public static partial Task<JSObject> OpfsRead(string path);

        [JSImport("byteLength", "varve-browser-tests")]
        public static partial int ByteLength(JSObject bytes);

        [JSImport("copyOut", "varve-browser-tests")]
        public static partial void CopyOut(JSObject bytes, [JSMarshalAs<JSType.MemoryView>] Span<byte> destination);

        [JSImport("userAgent", "varve-browser-tests")]
        public static partial string UserAgent();
    }
}
