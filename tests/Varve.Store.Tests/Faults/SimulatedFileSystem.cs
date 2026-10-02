// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Store.Tests.Faults;

/// <summary>The process dies here: every operation after it fails, and the machine restarts from an image.</summary>
internal sealed class SimulatedCrash : Exception
{
    public SimulatedCrash()
        : base("Simulated crash.")
    {
    }

    public SimulatedCrash(string message)
        : base(message)
    {
    }

    public SimulatedCrash(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>How a crash image keeps what was written (milestone 6a, failure injection).</summary>
internal enum CrashKind
{
    /// <summary>
    /// The process dies; the operating system does not. Every completed write
    /// is kept, the write in flight is cut at a byte, and metadata is as the
    /// process left it.
    /// </summary>
    Process,

    /// <summary>
    /// Power is lost. Data flushed is kept; each write since its file's last
    /// flush is kept whole, dropped, or torn at a 512-byte sector — in any
    /// combination, which is any reordering. Metadata operations are journaled
    /// in order: those before the last flush of any file are kept, and the rest
    /// are cut at any point (ext4, XFS and btrfs, ADR 0073).
    /// </summary>
    PowerLoss,

    /// <summary>
    /// As <see cref="PowerLoss"/>, but metadata is cut at any point, even
    /// before the last flush: a file system that does not make a new file's
    /// directory entry durable when the file is flushed (NTFS and APFS as far
    /// as their documentation goes, ADR 0073). Commits acknowledged into a lost
    /// file are lost; the store must still open consistently.
    /// </summary>
    LostDirectoryEntries,
}

/// <summary>
/// A file system in memory that counts its operations, can crash at any of
/// them — part way through a write included — and gives back the image a
/// restart would find (ADR 0073's model of each file system).
/// </summary>
/// <remarks>
/// Files are inodes: a handle keeps the file it opened even after a rename
/// over its name or a delete, as on Unix. The store's file backend runs on it
/// unchanged, through <see cref="IFileSystem"/>, the one double milestone 6a
/// allows: the process boundary.
/// </remarks>
internal sealed class SimulatedFileSystem : IFileSystem
{
    private readonly Dictionary<string, Inode> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _locks = new(StringComparer.Ordinal);
    private readonly List<MetadataOp> _journal = [];
    private readonly Lock _gate = new();
    private int _durableJournal;

    /// <summary>Called before each mutating operation with its number; returns the bytes of a write to keep when it crashes there, or null to go on.</summary>
    public Func<Operation, int?>? Injector { get; set; }

    /// <summary>How many mutating operations have been performed.</summary>
    public int Operations { get; private set; }

    /// <summary>Every write so far: its operation number and length, for choosing where to crash.</summary>
    public List<(int Operation, int Length, string Path)> Writes { get; } = [];

    public bool Crashed { get; private set; }

    public bool DirectoryExists(string path)
    {
        lock (_gate)
        {
            Live();
            return _directories.Contains(Normalise(path));
        }
    }

    public void CreateDirectory(string path)
    {
        lock (_gate)
        {
            Live();
            string normalised = Normalise(path);

            while (normalised.Length > 0 && _directories.Add(normalised))
            {
                normalised = Path.GetDirectoryName(normalised) ?? string.Empty;
            }
        }
    }

    public bool FileExists(string path)
    {
        lock (_gate)
        {
            Live();
            return _files.ContainsKey(Normalise(path));
        }
    }

    public IReadOnlyList<string> Files(string directory)
    {
        lock (_gate)
        {
            Live();
            string prefix = Normalise(directory) + Path.DirectorySeparatorChar;
            return [.. _files.Keys.Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        }
    }

    public IFileHandle Open(string path, FileOpen mode)
    {
        lock (_gate)
        {
            Live();
            string normalised = Normalise(path);

            switch (mode)
            {
                case FileOpen.CreateNew:
                    if (_files.ContainsKey(normalised))
                    {
                        throw new IOException("The file '" + path + "' already exists.");
                    }

                    Step(new Operation(OperationKind.Create, normalised, 0));
                    Inode created = new();
                    _files[normalised] = created;
                    _journal.Add(MetadataOp.Bind(normalised, created));
                    return new Handle(this, created, writable: true);

                case FileOpen.Write:
                    Inode existing = Get(normalised);

                    if (existing.ReadOnly)
                    {
                        throw new UnauthorizedAccessException("The file '" + path + "' is read-only.");
                    }

                    return new Handle(this, existing, writable: true);

                default:
                    return new Handle(this, Get(normalised), writable: false);
            }
        }
    }

    public void Move(string from, string to)
    {
        lock (_gate)
        {
            Live();
            string source = Normalise(from);
            string target = Normalise(to);
            Inode inode = Get(source);
            Step(new Operation(OperationKind.Move, target, 0));
            _files.Remove(source);
            _files[target] = inode;
            _journal.Add(MetadataOp.Unbind(source));
            _journal.Add(MetadataOp.Bind(target, inode));
        }
    }

    public void Delete(string path)
    {
        lock (_gate)
        {
            Live();
            string normalised = Normalise(path);

            if (!_files.ContainsKey(normalised))
            {
                return;
            }

            Step(new Operation(OperationKind.Delete, normalised, 0));
            _files.Remove(normalised);
            _journal.Add(MetadataOp.Unbind(normalised));
        }
    }

    public bool IsReadOnly(string path)
    {
        lock (_gate)
        {
            Live();
            return Get(Normalise(path)).ReadOnly;
        }
    }

    public void MakeReadOnly(string path)
    {
        lock (_gate)
        {
            Live();
            Inode inode = Get(Normalise(path));
            Step(new Operation(OperationKind.Attribute, Normalise(path), 0));
            inode.ReadOnly = true;
            _journal.Add(MetadataOp.Seal(inode));
        }
    }

    public IDisposable? TryLock(string path)
    {
        lock (_gate)
        {
            Live();
            string normalised = Normalise(path);

            if (!_locks.Add(normalised))
            {
                return null;
            }

            if (!_files.ContainsKey(normalised))
            {
                Inode inode = new();
                _files[normalised] = inode;
                _journal.Add(MetadataOp.Bind(normalised, inode));
            }

            return new Releaser(this, normalised);
        }
    }

    /// <summary>
    /// What a restart finds, as a new file system. <paramref name="random"/>
    /// chooses, for a power loss, which unflushed writes survive and where
    /// metadata is cut.
    /// </summary>
    public SimulatedFileSystem Image(CrashKind kind, Random random)
    {
        lock (_gate)
        {
            SimulatedFileSystem image = new();

            foreach (string directory in _directories)
            {
                image._directories.Add(directory);
            }

            // Metadata: the journal replayed up to a cut.
            int keep = kind switch
            {
                CrashKind.Process => _journal.Count,
                CrashKind.PowerLoss => random.Next(_durableJournal, _journal.Count + 1),
                _ => random.Next(0, _journal.Count + 1),
            };

            Dictionary<string, Inode> names = new(StringComparer.Ordinal);
            Dictionary<Inode, bool> sealedAt = [];

            for (int i = 0; i < keep; i++)
            {
                MetadataOp op = _journal[i];

                switch (op.Kind)
                {
                    case MetadataKind.Bind:
                        names[op.Path!] = op.Inode!;
                        break;
                    case MetadataKind.Unbind:
                        names.Remove(op.Path!);
                        break;
                    default:
                        sealedAt[op.Inode!] = true;
                        break;
                }
            }

            Dictionary<Inode, Inode> copies = [];

            foreach ((string path, Inode inode) in names)
            {
                if (!copies.TryGetValue(inode, out Inode? copy))
                {
                    copy = new Inode { ReadOnly = sealedAt.ContainsKey(inode) };
                    copy.Bytes.AddRange(kind == CrashKind.Process ? inode.Bytes : inode.Survivors(random));
                    copy.Durable = [.. copy.Bytes];
                    copies[inode] = copy;
                }

                image._files[path] = copy;
                image._journal.Add(MetadataOp.Bind(path, copy));
            }

            image._durableJournal = image._journal.Count;
            return image;
        }
    }

    /// <summary>A copy of every file as it reads now, for a copier that reads one file at a time.</summary>
    public byte[] Read(string path)
    {
        lock (_gate)
        {
            return [.. Get(Normalise(path)).Bytes];
        }
    }

    /// <summary>Adds a file with given bytes, durable, as a copier writes it.</summary>
    public void Place(string path, byte[] bytes, bool readOnly)
    {
        lock (_gate)
        {
            string normalised = Normalise(path);
            CreateDirectory(Path.GetDirectoryName(normalised)!);
            Inode inode = new() { ReadOnly = readOnly };
            inode.Bytes.AddRange(bytes);
            inode.Durable = [.. bytes];
            _files[normalised] = inode;
            _journal.Add(MetadataOp.Bind(normalised, inode));
            _durableJournal = _journal.Count;
        }
    }

    public void RemoveTree(string directory)
    {
        lock (_gate)
        {
            string prefix = Normalise(directory);

            foreach (string path in _files.Keys.Where(p => p == prefix || p.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToList())
            {
                _files.Remove(path);
                _journal.Add(MetadataOp.Unbind(path));
            }

            _directories.RemoveWhere(d => d == prefix || d.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            _durableJournal = _journal.Count;
        }
    }

    private static string Normalise(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private Inode Get(string path) =>
        _files.TryGetValue(path, out Inode? inode) ? inode : throw new FileNotFoundException("No file '" + path + "'.", path);

    private void Live()
    {
        if (Crashed)
        {
            throw new SimulatedCrash("The process has crashed.");
        }
    }

    // Counts a mutating operation and crashes when the injector says so,
    // returning how much of a write to keep. Null means carry on.
    private int? Step(Operation operation)
    {
        Operations++;
        int? keep = Injector?.Invoke(operation with { Number = Operations });

        if (keep is not null && operation.Kind != OperationKind.Write)
        {
            Crashed = true;
            throw new SimulatedCrash("Crashed before " + operation.Kind + " of " + operation.Path + ".");
        }

        return keep;
    }

    private void Write(Inode inode, long offset, ReadOnlySpan<byte> bytes, string path)
    {
        lock (_gate)
        {
            Live();
            int? keep = Step(new Operation(OperationKind.Write, path, bytes.Length));
            Writes.Add((Operations, bytes.Length, path));
            ReadOnlySpan<byte> written = keep is int k ? bytes[..Math.Min(k, bytes.Length)] : bytes;
            inode.Write(offset, written);

            if (keep is not null)
            {
                Crashed = true;
                throw new SimulatedCrash("Crashed after " + written.Length + " of " + bytes.Length + " bytes to " + path + ".");
            }
        }
    }

    private void Flush(Inode inode, string path)
    {
        lock (_gate)
        {
            Live();
            Step(new Operation(OperationKind.Flush, path, 0));
            inode.Flush();
            _durableJournal = _journal.Count;
        }
    }

    private string PathOf(Inode inode)
    {
        foreach ((string path, Inode candidate) in _files)
        {
            if (ReferenceEquals(candidate, inode))
            {
                return path;
            }
        }

        return "(unlinked)";
    }

    internal enum OperationKind
    {
        Create,
        Write,
        Flush,
        Move,
        Delete,
        Attribute,
    }

    internal readonly record struct Operation(OperationKind Kind, string Path, int Length)
    {
        public int Number { get; init; }
    }

    private enum MetadataKind
    {
        Bind,
        Unbind,
        Seal,
    }

    private sealed record MetadataOp(MetadataKind Kind, string? Path, Inode? Inode)
    {
        public static MetadataOp Bind(string path, Inode inode) => new(MetadataKind.Bind, path, inode);

        public static MetadataOp Unbind(string path) => new(MetadataKind.Unbind, path, null);

        public static MetadataOp Seal(Inode inode) => new(MetadataKind.Seal, null, inode);
    }

    /// <summary>A file's bytes as read, its bytes as flushed, and the writes in between.</summary>
    private sealed class Inode
    {
        public List<byte> Bytes { get; } = [];

        public byte[] Durable { get; set; } = [];

        public List<(long Offset, byte[] Bytes)> Pending { get; } = [];

        public bool ReadOnly { get; set; }

        public void Write(long offset, ReadOnlySpan<byte> bytes)
        {
            while (Bytes.Count < offset + bytes.Length)
            {
                Bytes.Add(0);
            }

            for (int i = 0; i < bytes.Length; i++)
            {
                Bytes[(int)offset + i] = bytes[i];
            }

            Pending.Add((offset, bytes.ToArray()));
        }

        public void Flush()
        {
            Durable = [.. Bytes];
            Pending.Clear();
        }

        /// <summary>The flushed bytes, with each later write kept, dropped or torn at a sector.</summary>
        public List<byte> Survivors(Random random)
        {
            List<byte> bytes = [.. Durable];

            foreach ((long offset, byte[] written) in Pending)
            {
                int choice = random.Next(3);

                if (choice == 0)
                {
                    continue;
                }

                int length = written.Length;

                if (choice == 2)
                {
                    // Torn: the sectors before some boundary reached the disk.
                    long end = offset + random.Next(length + 1);
                    length = (int)Math.Max(0, (end / 512 * 512) - offset);
                }

                while (bytes.Count < offset + length)
                {
                    bytes.Add(0);
                }

                for (int i = 0; i < length; i++)
                {
                    bytes[(int)offset + i] = written[i];
                }
            }

            return bytes;
        }
    }

    private sealed class Handle(SimulatedFileSystem owner, Inode inode, bool writable) : IFileHandle
    {
        public long Length
        {
            get
            {
                lock (owner._gate)
                {
                    return inode.Bytes.Count;
                }
            }
        }

        public ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            if (!writable)
            {
                throw new UnauthorizedAccessException("The handle is read-only.");
            }

            owner.Write(inode, offset, bytes.Span, owner.PathOf(inode));
            return ValueTask.CompletedTask;
        }

        public int Read(long offset, Span<byte> destination)
        {
            lock (owner._gate)
            {
                owner.Live();
                int count = (int)Math.Max(0, Math.Min(destination.Length, inode.Bytes.Count - offset));

                for (int i = 0; i < count; i++)
                {
                    destination[i] = inode.Bytes[(int)offset + i];
                }

                return count;
            }
        }

        public void Flush() => owner.Flush(inode, owner.PathOf(inode));

        public void Dispose()
        {
        }
    }

    private sealed class Releaser(SimulatedFileSystem owner, string path) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._locks.Remove(path);
            }
        }
    }
}
