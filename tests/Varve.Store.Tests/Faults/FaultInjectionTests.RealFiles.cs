// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Store.Tests.Faults;

/// <summary>
/// The faults on the file system the store ships on (ADR 0214 points 3 and
/// 4): a power cut on real files, decided by ADR 0073's models, and a real
/// process killed at every flush. The store opens the result through
/// <see cref="DiskFileSystem"/>, the backend's own code.
/// </summary>
public static partial class FaultInjectionTests
{
    /// <summary>A power cut on real files, at every operation.</summary>
    public sealed class RealPowerCut
    {
        /// <summary>
        /// The workload runs on the real disk while every operation is
        /// mirrored into a <see cref="SimulatedFileSystem"/>. At a cut the
        /// real directory is replaced by the image the mirror gives under the
        /// power-loss model and the lost-entries model, and the store opens it
        /// through <see cref="DiskFileSystem"/>. <c>VARVE_FAULT_REAL_POWER_STRIDE=1</c>
        /// cuts at every operation.
        /// </summary>
        [Theory]
        [InlineData(6701, 16)]
        public async Task a_power_cut_on_real_files_recovers_to_the_last_closed_commit(int seed, int steps)
        {
            Sweep sweep = Depth.SweepOf("FAULT_REAL_POWER", 8);
            Report(sweep.Replay);
            Reference reference = await ReferenceAsync(seed, steps);
            string scratch = Path.Combine(Path.GetTempPath(), "varve-real-faults", Guid.NewGuid().ToString("N"));
            int points = 0;

            try
            {
                for (int operation = sweep.First; operation <= reference.Operations; operation += sweep.Stride)
                {
                    int at = operation;
                    string written = Path.Combine(scratch, "written-" + at.ToString(CultureInfo.InvariantCulture));
                    SimulatedFileSystem mirror = new() { Injector = op => op.Number == at ? 0 : null };
                    long acknowledged = 0;

                    using (TeeFileSystem files = new(mirror))
                    {
                        try
                        {
                            await RunAsync(files, seed, steps, p => acknowledged = p, null, root: written);
                        }
                        catch (SimulatedCrash)
                        {
                        }
                    }

                    foreach ((CrashKind kind, int image) in new[] { (CrashKind.PowerLoss, 0), (CrashKind.PowerLoss, 1), (CrashKind.LostDirectoryEntries, 0) })
                    {
                        string restarted = Path.Combine(scratch, "restarted-" + at.ToString(CultureInfo.InvariantCulture) + "-" + kind + "-" + image.ToString(CultureInfo.InvariantCulture));
                        Materialise(mirror.Image(kind, new Random((at * 31) + image)), written, restarted);
                        string where = sweep.Replay + ": real power cut at operation " + at + ", " + kind + " image " + image;
                        await VerifyCoreAsync(new DiskFileSystem(), reference, kind == CrashKind.LostDirectoryEntries ? 0 : acknowledged, where, null, "after", root: restarted);
                        Directory.Delete(restarted, recursive: true);
                        points++;
                    }

                    Directory.Delete(written, recursive: true);
                }
            }
            finally
            {
                DeleteTree(scratch);
            }

            Report(sweep.Replay + ": real power cut, seed " + seed + ": " + reference.Operations + " operations, " + points + " images");
        }

        // The image's files under `from`, written as real files under `to`,
        // sealed where the image says.
        private static void Materialise(SimulatedFileSystem image, string from, string to)
        {
            Directory.CreateDirectory(to);

            foreach (string file in image.Files(from))
            {
                string target = Path.Combine(to, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, image.Read(file));

                if (image.IsReadOnly(file))
                {
                    File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
                }
            }
        }
    }

    /// <summary>A real process, killed at every flush, just before it and just after.</summary>
    public sealed class RealKill
    {
        /// <summary>
        /// The workload runs in a child process — this test assembly, run as
        /// the host <see cref="KillHost"/> — on the real disk, and the child
        /// kills itself at a flush, just before it and in a second run just
        /// after it (ADR 0214 point 3). The parent opens the real directory
        /// through <see cref="DiskFileSystem"/>: every commit the child
        /// acknowledged is there, nothing past a closed commit is, and the log
        /// continues. <c>VARVE_FAULT_KILL_STRIDE=1</c> kills at every flush.
        /// </summary>
        [Theory]
        [InlineData(6801, 16)]
        public async Task a_process_killed_at_any_flush_recovers_to_the_last_closed_commit(int seed, int steps)
        {
            Sweep sweep = Depth.SweepOf("FAULT_KILL", 25);
            Report(sweep.Replay);
            Reference reference = await ReferenceAsync(seed, steps);
            string scratch = Path.Combine(Path.GetTempPath(), "varve-real-faults", Guid.NewGuid().ToString("N"));
            int points = 0;

            try
            {
                // A run that is not killed counts the workload's flushes.
                string counted = Path.Combine(scratch, "counted");
                (int exit, _, string output) = await HostAsync(counted, seed, steps, 0, "before");
                Assert.True(exit == 0, "the host run that counts flushes failed: " + output);
                int flushes = int.Parse(File.ReadAllText(Path.Combine(counted, KillHost.FlushesFile)), CultureInfo.InvariantCulture);
                Assert.True(flushes > 0, "the workload flushed nothing");

                for (int at = sweep.First; at <= flushes; at += sweep.Stride)
                {
                    foreach (string when in new[] { "before", "after" })
                    {
                        string directory = Path.Combine(scratch, at.ToString(CultureInfo.InvariantCulture) + "-" + when);
                        (int killedExit, long acknowledged, string killedOutput) = await HostAsync(directory, seed, steps, at, when);
                        string where = sweep.Replay + ": killed " + when + " flush " + at + " of " + flushes;
                        Assert.True(killedExit != 0, where + ": the host was not killed: " + killedOutput);
                        await VerifyCoreAsync(new DiskFileSystem(), reference, acknowledged, where, null, "after", root: Path.Combine(directory, "dataset"));
                        Directory.Delete(directory, recursive: true);
                        points++;
                    }
                }

                Report(sweep.Replay + ": real kill, seed " + seed + ": " + flushes + " flushes, " + points + " kills");
            }
            finally
            {
                DeleteTree(scratch);
            }
        }

        // Runs the host in a child process; its exit code, the last position
        // it acknowledged, and what it printed.
        private static async Task<(int Exit, long Acknowledged, string Output)> HostAsync(string directory, int seed, int steps, int killAt, string when)
        {
            Directory.CreateDirectory(directory);
            string assembly = typeof(FaultInjectionTests).Assembly.Location;
            string host = Environment.ProcessPath!;
            ProcessStartInfo start = new(host)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            // Under `dotnet exec` the assembly is an argument; as its own
            // apphost it is the process.
            if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                start.ArgumentList.Add("exec");
                start.ArgumentList.Add(assembly);
            }
            else if (!Path.GetFileNameWithoutExtension(host).Equals(Path.GetFileNameWithoutExtension(assembly), StringComparison.OrdinalIgnoreCase))
            {
                start.FileName = "dotnet";
                start.ArgumentList.Add("exec");
                start.ArgumentList.Add(assembly);
            }

            start.ArgumentList.Add("-method");
            start.ArgumentList.Add(typeof(KillHost).FullName + "." + nameof(KillHost.hosts_the_workload_a_kill_test_runs));
            start.Environment[KillHost.Directory] = directory;
            start.Environment[KillHost.Seed] = seed.ToString(CultureInfo.InvariantCulture);
            start.Environment[KillHost.Steps] = steps.ToString(CultureInfo.InvariantCulture);
            start.Environment[KillHost.At] = killAt.ToString(CultureInfo.InvariantCulture);
            start.Environment[KillHost.When] = when;

            using Process child = Process.Start(start)!;
            Task<string> output = child.StandardOutput.ReadToEndAsync();
            Task<string> error = child.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
            await child.WaitForExitAsync(timeout.Token);
            string acks = Path.Combine(directory, KillHost.AcknowledgedFile);
            long acknowledged = File.Exists(acks) ? File.ReadAllLines(acks).Where(l => l.Length > 0).Select(l => long.Parse(l, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max() : 0;
            return (child.ExitCode, acknowledged, await output + await error);
        }
    }

    /// <summary>
    /// The host <see cref="RealKill"/> launches: this assembly, run in a child
    /// process with the environment that names its directory, seed and kill
    /// point. In a test run without that environment it is skipped.
    /// </summary>
    public sealed class KillHost
    {
        internal const string Directory = "VARVE_KILL_HOST_DIRECTORY";
        internal const string Seed = "VARVE_KILL_HOST_SEED";
        internal const string Steps = "VARVE_KILL_HOST_STEPS";
        internal const string At = "VARVE_KILL_HOST_AT";
        internal const string When = "VARVE_KILL_HOST_WHEN";
        internal const string FlushesFile = "flushes";
        internal const string AcknowledgedFile = "acknowledged";

        [Fact]
        public async Task hosts_the_workload_a_kill_test_runs()
        {
            string? directory = Environment.GetEnvironmentVariable(Directory);
            Assert.SkipWhen(directory is null, "The host RealKill runs in a child process; not a test of its own.");
            int seed = int.Parse(Environment.GetEnvironmentVariable(Seed)!, CultureInfo.InvariantCulture);
            int steps = int.Parse(Environment.GetEnvironmentVariable(Steps)!, CultureInfo.InvariantCulture);
            int at = int.Parse(Environment.GetEnvironmentVariable(At)!, CultureInfo.InvariantCulture);
            bool after = Environment.GetEnvironmentVariable(When) == "after";
            string acks = Path.Combine(directory!, AcknowledgedFile);
            KillingFileSystem files = new(at, after);

            // Each acknowledgement is in the page cache before the next
            // operation, so a kill never loses one the parent must count.
            await RunAsync(files, seed, steps, p => File.AppendAllText(acks, p.ToString(CultureInfo.InvariantCulture) + "\n"), null, root: Path.Combine(directory!, "dataset"));
            File.WriteAllText(Path.Combine(directory!, FlushesFile), files.Flushes.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void DeleteTree(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(directory, recursive: true);
    }

    /// <summary>The disk, which kills its own process at a flush: just before it, or just after it.</summary>
    private sealed class KillingFileSystem(int at, bool after) : IFileSystem
    {
        private readonly DiskFileSystem _disk = new();
        private int _flushes;

        public int Flushes => Volatile.Read(ref _flushes);

        public bool DirectoryExists(string path) => _disk.DirectoryExists(path);

        public void CreateDirectory(string path) => _disk.CreateDirectory(path);

        public bool FileExists(string path) => _disk.FileExists(path);

        public IReadOnlyList<string> Files(string directory) => _disk.Files(directory);

        public IFileHandle Open(string path, FileOpen mode) => new Handle(this, _disk.Open(path, mode));

        public void Move(string from, string to) => _disk.Move(from, to);

        public void Delete(string path) => _disk.Delete(path);

        public bool DeleteEmptyDirectory(string path) => _disk.DeleteEmptyDirectory(path);

        public bool IsReadOnly(string path) => _disk.IsReadOnly(path);

        public void MakeReadOnly(string path) => _disk.MakeReadOnly(path);

        public IDisposable? TryLock(string path) => _disk.TryLock(path);

        private void Flushing(IFileHandle file)
        {
            int number = Interlocked.Increment(ref _flushes);

            if (number == at && !after)
            {
                Process.GetCurrentProcess().Kill();
            }

            file.Flush();

            if (number == at && after)
            {
                Process.GetCurrentProcess().Kill();
            }
        }

        private sealed class Handle(KillingFileSystem owner, IFileHandle file) : IFileHandle
        {
            public long Length => file.Length;

            public ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) => file.WriteAsync(offset, bytes, cancellationToken);

            public int Read(long offset, Span<byte> destination) => file.Read(offset, destination);

            public void Flush() => owner.Flushing(file);

            public void Dispose() => file.Dispose();
        }
    }

    /// <summary>
    /// The disk, with every operation mirrored first into a simulated file
    /// system, which decides where the power is cut and what survives. A cut
    /// closes every real handle and lock, as a power loss would.
    /// </summary>
    private sealed class TeeFileSystem(SimulatedFileSystem mirror) : IFileSystem, IDisposable
    {
        private readonly DiskFileSystem _disk = new();
        private readonly List<IDisposable> _open = [];
        private readonly Lock _gate = new();

        public bool DirectoryExists(string path) => _disk.DirectoryExists(path);

        public void CreateDirectory(string path)
        {
            mirror.CreateDirectory(path);
            _disk.CreateDirectory(path);
        }

        public bool FileExists(string path) => _disk.FileExists(path);

        public IReadOnlyList<string> Files(string directory) => _disk.Files(directory);

        public IFileHandle Open(string path, FileOpen mode)
        {
            IFileHandle mirrored = mirror.Open(path, mode);
            IFileHandle real = _disk.Open(path, mode);
            return Track(new Handle(mirrored, real));
        }

        public void Move(string from, string to)
        {
            mirror.Move(from, to);
            _disk.Move(from, to);
        }

        public void Delete(string path)
        {
            mirror.Delete(path);
            _disk.Delete(path);
        }

        public bool DeleteEmptyDirectory(string path)
        {
            mirror.DeleteEmptyDirectory(path);
            return _disk.DeleteEmptyDirectory(path);
        }

        public bool IsReadOnly(string path) => _disk.IsReadOnly(path);

        public void MakeReadOnly(string path)
        {
            mirror.MakeReadOnly(path);
            _disk.MakeReadOnly(path);
        }

        public IDisposable? TryLock(string path)
        {
            IDisposable? mirrored = mirror.TryLock(path);

            if (mirrored is null)
            {
                return null;
            }

            IDisposable? real = _disk.TryLock(path);

            if (real is null)
            {
                mirrored.Dispose();
                return null;
            }

            return Track(new Both(mirrored, real));
        }

        // What the power cut leaves open is closed, so that the directory
        // can be replaced, on Windows too.
        public void Dispose()
        {
            lock (_gate)
            {
                foreach (IDisposable open in _open)
                {
                    open.Dispose();
                }

                _open.Clear();
            }
        }

        private T Track<T>(T open)
            where T : IDisposable
        {
            lock (_gate)
            {
                _open.Add(open);
            }

            return open;
        }

        private sealed class Both(IDisposable mirrored, IDisposable real) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                real.Dispose();

                try
                {
                    mirrored.Dispose();
                }
                catch (SimulatedCrash)
                {
                }
            }
        }

        private sealed class Handle(IFileHandle mirrored, IFileHandle real) : IFileHandle
        {
            private bool _disposed;

            public long Length => real.Length;

            public async ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
            {
                await mirrored.WriteAsync(offset, bytes, cancellationToken).ConfigureAwait(false);
                await real.WriteAsync(offset, bytes, cancellationToken).ConfigureAwait(false);
            }

            public int Read(long offset, Span<byte> destination) => real.Read(offset, destination);

            public void Flush()
            {
                mirrored.Flush();
                real.Flush();
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                real.Dispose();

                try
                {
                    mirrored.Dispose();
                }
                catch (SimulatedCrash)
                {
                }
            }
        }
    }
}
