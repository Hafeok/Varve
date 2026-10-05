// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Durability, measured on the machine it runs on.
//
//   dotnet run eng/durability.cs -- [--directory <path>] [--appends <n>]
//
// ADR 0073 declares the file backend `Synchronised`: a commit returns after
// its record bytes are flushed to the device. What that costs depends on the
// platform and its file system, and ADR 0073 records it per platform. This
// measures it with the calls the file backend makes (`DiskFileSystem` in
// src/Varve.Store/FileSystem.cs): `RandomAccess.Write` at the end of a file
// opened as the backend opens a segment, then `RandomAccess.FlushToDisk`.
// It references no Varve project, so it runs whatever state the build is in.
//
// Two shapes:
// - a small record, appended and flushed, which is what a single-quad commit
//   costs on top of the store's own work;
// - a 64 KiB record, appended and flushed, which shows whether the cost is the
//   flush or the bytes.
//
// It is a measurement, not a gate: it exits 0 whatever it measures, and 2 only
// when it could not run. In CI it runs on every platform with a runner and
// writes its table to the step summary; the numbers are recorded by hand in
// ADR 0073 and tests/Varve.Benchmarks/README.md, with the run they came from.
// A shared runner is a noisy machine (ADR 0027), so a number from it is an
// order of magnitude, not a benchmark.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

string? directory = null;
int appends = 1000;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--directory" when i + 1 < args.Length:
            directory = args[++i];
            break;
        case "--appends" when i + 1 < args.Length:
            appends = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        default:
            Console.Error.WriteLine("durability: unknown argument '" + args[i] + "'.");
            return 2;
    }
}

directory ??= Path.Combine(Path.GetTempPath(), "varve-durability-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

try
{
    Directory.CreateDirectory(directory);
    string fileSystem = FileSystemOf(directory);

    List<string> rows =
    [
        Measure(directory, "a 256-byte record, appended and flushed", 256, appends),
        Measure(directory, "a 64 KiB record, appended and flushed", 64 * 1024, Math.Max(10, appends / 10)),
    ];

    string report = string.Join(
        "\n",
        [
            "## Durability: append and flush to the device",
            string.Empty,
            "Platform: " + RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + "), file system: " + fileSystem + ", .NET " + Environment.Version + ".",
            string.Empty,
            "| Shape | Flushes | Median | 90th | 99th | Mean |",
            "|---|---:|---:|---:|---:|---:|",
            .. rows,
        ]);

    Console.WriteLine(report);

    if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
    {
        File.AppendAllText(summary, report + "\n");
    }

    return 0;
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("durability: could not run: " + e.Message);
    return 2;
}
finally
{
    try
    {
        Directory.Delete(directory, recursive: true);
    }
    catch (IOException)
    {
    }
}

static string Measure(string directory, string shape, int recordBytes, int count)
{
    string path = Path.Combine(directory, recordBytes.ToString(CultureInfo.InvariantCulture) + ".seg");
    byte[] record = new byte[recordBytes];
    new Random(73).NextBytes(record);
    double[] milliseconds = new double[count];

    // Opened as the file backend opens a new segment.
    using (SafeFileHandle handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete))
    {
        long length = 0;

        for (int i = 0; i < count; i++)
        {
            long start = Stopwatch.GetTimestamp();
            RandomAccess.Write(handle, record, length);
            RandomAccess.FlushToDisk(handle);
            milliseconds[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            length += recordBytes;
        }
    }

    Array.Sort(milliseconds);
    return string.Create(
        CultureInfo.InvariantCulture,
        $"| {shape} | {count:N0} | {Percentile(milliseconds, 0.50):F3} ms | {Percentile(milliseconds, 0.90):F3} ms | {Percentile(milliseconds, 0.99):F3} ms | {milliseconds.Average():F3} ms |");
}

static double Percentile(double[] sorted, double p) => sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];

// The file system the directory is on: the drive whose root is the longest
// prefix of the directory's full path.
static string FileSystemOf(string directory)
{
    string full = Path.GetFullPath(directory);
    DriveInfo? best = null;

    foreach (DriveInfo drive in DriveInfo.GetDrives())
    {
        string root = drive.RootDirectory.FullName;

        if (full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && (best is null || root.Length > best.RootDirectory.FullName.Length))
        {
            best = drive;
        }
    }

    try
    {
        return best is null ? "unknown" : best.DriveFormat + " at " + best.RootDirectory.FullName;
    }
    catch (IOException)
    {
        return "unknown";
    }
}
