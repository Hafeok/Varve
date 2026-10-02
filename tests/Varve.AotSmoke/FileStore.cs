// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.AotSmoke;

/// <summary>
/// A file-backed dataset under Native AOT: created, committed to, crashed by
/// killing the process that holds it mid-commit and by a torn write injected
/// at the end of its newest segment, reopened, and read (milestone 6a).
/// </summary>
/// <remarks>
/// The crash is real: a child process — this binary, with
/// <c>--commit-until-killed</c> — commits in a loop and prints each position it
/// was told is durable, and the parent kills it. The child holds the lease, so
/// the parent also proves a second opener is refused while it lives and that
/// the operating system releases the lease when it dies (ADR 0075).
/// </remarks>
internal static class FileStore
{
    internal const string ChildArgument = "--commit-until-killed";

    private static readonly RdfTerm Predicate = RdfTerm.Iri("http://example.org/p"u8);

    private static DatasetOptions Options => new()
    {
        Clock = TimeProvider.System,
        SegmentBytes = new ByteCount(4096),
        MemtableLimit = new QuadCount(8),
        Maintenance = MaintenanceMode.Off,
    };

    private static FileStorageOptions StorageOptions => new() { Clock = TimeProvider.System };

    private static CommitRequest Numbered(long n) =>
        new CommitRequest().Assert(RdfTerm.Iri("http://example.org/s"u8), Predicate, RdfTerm.Literal(System.Text.Encoding.UTF8.GetBytes(n.ToString(CultureInfo.InvariantCulture))));

    /// <summary>The child: open, commit until killed, print every acknowledged position.</summary>
    internal static async Task<int> CommitUntilKilledAsync(string directory)
    {
        await using FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), StorageOptions);
        await using Dataset dataset = await Dataset.OpenAsync(storage, Options);
        Console.WriteLine("held");

        for (long n = 1000; ; n++)
        {
            CommitResult result = await dataset.CommitAsync(Numbered(n));
            Console.WriteLine("ack " + result.Position.Value.ToString(CultureInfo.InvariantCulture));
            await dataset.MaintainAsync();
        }
    }

    internal static async Task<int> RunAsync()
    {
        string directory = Directory.CreateTempSubdirectory("varve-aot-").FullName;

        try
        {
            return await RunAsync(directory);
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<int> RunAsync(string directory)
    {
        DatasetDirectory dataset = new(directory);

        await using (FileStorage storage = await FileStorage.OpenAsync(dataset, StorageOptions))
        await using (Dataset created = await Dataset.CreateAsync(storage, new DatasetId(Guid.NewGuid()), Options))
        {
            for (int i = 0; i < 20; i++)
            {
                await created.CommitAsync(Numbered(i));
                await created.MaintainAsync();
            }

            await created.CheckpointAsync(new Position(10));
        }

        // A crash: the child commits until it is killed.
        long acknowledged = await KillAChildMidCommitAsync(directory);

        if (acknowledged < 0)
        {
            return 1;
        }

        // A torn write at the end of the newest segment, as power loss leaves one.
        string newest = Directory.GetFiles(Path.Combine(directory, "log"), "*.seg")[^1];
        File.SetAttributes(newest, FileAttributes.Normal);

        await using (FileStream torn = new(newest, FileMode.Append))
        {
            torn.Write(new byte[] { 0x7F, 0, 0, 0, 0, 1, 0, 0, 0x2A, 0x2A, 0x2A });
        }

        await using FileStorage reopenedStorage = await FileStorage.OpenAsync(dataset, StorageOptions);
        await using Dataset reopened = await Dataset.OpenAsync(reopenedStorage, Options);
        using DatasetView view = reopened.Pin();
        int quads = CountQuads(view);
        CommitResult next = await reopened.CommitAsync(Numbered(1_000_000));

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"files: killed after {acknowledged} acknowledged, reopened at {reopened.Head.Value - 1} with {quads} quads past a torn tail, committed {next.Position.Value}, durability {reopened.Durability}"));

        if (reopened.Head.Value - 1 < acknowledged || quads != reopened.Head.Value - 1 || next.Outcome != CommitOutcome.Committed || reopened.Durability != Durability.Synchronised)
        {
            Console.Error.WriteLine("aot-smoke: the file-backed store did not recover to its last acknowledged commit.");
            return 1;
        }

        return 0;
    }

    // The child's last acknowledged position, or -1 when the lease or the
    // kill did not behave.
    private static async Task<long> KillAChildMidCommitAsync(string directory)
    {
        ProcessStartInfo start = new(Environment.ProcessPath!, [ChildArgument, directory])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using Process child = Process.Start(start)!;
        long acknowledged = 0;
        string? line;

        while ((line = await child.StandardOutput.ReadLineAsync()) is not null && line != "held")
        {
        }

        try
        {
            await using FileStorage refused = await FileStorage.OpenAsync(new DatasetDirectory(directory), StorageOptions);
            Console.Error.WriteLine("aot-smoke: a second opener was not refused while the child held the lease.");
            child.Kill();
            return -1;
        }
        catch (DatasetLeasedException)
        {
        }

        while ((line = await child.StandardOutput.ReadLineAsync()) is not null)
        {
            if (line.StartsWith("ack ", StringComparison.Ordinal))
            {
                acknowledged = long.Parse(line.AsSpan(4), CultureInfo.InvariantCulture);
            }

            if (acknowledged >= 60)
            {
                child.Kill();
                break;
            }
        }

        // Whatever it printed after the kill was acknowledged too.
        while ((line = await child.StandardOutput.ReadLineAsync()) is not null)
        {
            if (line.StartsWith("ack ", StringComparison.Ordinal))
            {
                acknowledged = long.Parse(line.AsSpan(4), CultureInfo.InvariantCulture);
            }
        }

        await child.WaitForExitAsync();
        Console.WriteLine("files: the lease refused a second opener while the child lived");
        return acknowledged;
    }

    private static int CountQuads(DatasetView source)
    {
        int count = 0;

        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }
}
