// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;

namespace Varve.Store.Tests.Objects;

/// <summary>Which of the five operations a request was.</summary>
internal enum ObjectOperation
{
    Create,
    Replace,
    Read,
    List,
    Delete,
}

/// <summary>
/// An object store in memory, linearisable by one lock: the adapter the
/// object-storage backend is proved against before any provider (ADR 0134).
/// It counts requests by operation, and a test may put a hook in front of
/// every request to fail it, or to run something before or after it.
/// </summary>
internal sealed class MemoryObjectStore : IObjectStore
{
    private readonly Lock _gate = new();
    private readonly SortedDictionary<string, (byte[] Bytes, long Version)> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<ObjectOperation, int> _counts = [];
    private long _versions;

    /// <summary>
    /// Runs before each request with its operation and name; may throw to
    /// fail it before the store sees it, and returns a hook to run after the
    /// store applied it (which may throw to lose the answer), or null.
    /// </summary>
    public Func<ObjectOperation, ObjectName, Action?>? Hook { get; set; }

    /// <summary>
    /// Whether the store ignores the conditions of its conditional writes,
    /// as Garage does with <c>If-None-Match: *</c>: a create overwrites and a
    /// replace succeeds against any version.
    /// </summary>
    public bool IgnoresConditions { get; set; }

    /// <summary>
    /// Whether the store enforces each condition one request at a time and
    /// not under concurrency: it checks, lets go, and then writes without
    /// checking again. Two creates that race both win, where a create after a
    /// create is refused. The open's probe cannot see it (ADR 0134); only the
    /// race property does (ADR 0183).
    /// </summary>
    public bool LosesRaces { get; set; }

    /// <summary>With <see cref="LosesRaces"/>: whether creates hold all the same, so that only replaces race.</summary>
    public bool CreatesHold { get; set; }

    public int Count(ObjectOperation operation)
    {
        lock (_gate)
        {
            return _counts.GetValueOrDefault(operation);
        }
    }

    public int Requests
    {
        get
        {
            lock (_gate)
            {
                int total = 0;

                foreach (int count in _counts.Values)
                {
                    total += count;
                }

                return total;
            }
        }
    }

    public void ResetCounts()
    {
        lock (_gate)
        {
            _counts.Clear();
        }
    }

    /// <summary>Every object's name, in order.</summary>
    public IReadOnlyList<string> Names()
    {
        lock (_gate)
        {
            return [.. _objects.Keys];
        }
    }

    /// <summary>An object's bytes, or null.</summary>
    public byte[]? Get(string name)
    {
        lock (_gate)
        {
            return _objects.TryGetValue(name, out var found) ? found.Bytes : null;
        }
    }

    /// <summary>Writes an object behind the backend's back: a test's way to damage or plant one.</summary>
    public void Put(string name, byte[] bytes)
    {
        lock (_gate)
        {
            _objects[name] = (bytes, ++_versions);
        }
    }

    public ValueTask<ObjectVersion?> CreateIfAbsentAsync(ObjectName name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        LosesRaces && !CreatesHold ? Racing(ObjectOperation.Create, name, () => !_objects.ContainsKey(name.Value), content) : Run(ObjectOperation.Create, name, () =>
        {
            if (_objects.ContainsKey(name.Value) && !IgnoresConditions)
            {
                return (ObjectVersion?)null;
            }

            _objects[name.Value] = (content.ToArray(), ++_versions);
            return Version(_versions);
        });

    public ValueTask<ObjectVersion?> ReplaceIfUnchangedAsync(ObjectName name, ObjectVersion expected, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        LosesRaces ? Racing(ObjectOperation.Replace, name, () => _objects.TryGetValue(name.Value, out var found) && Version(found.Version) == expected, content) : Run(ObjectOperation.Replace, name, () =>
        {
            if (!_objects.TryGetValue(name.Value, out var found) || (Version(found.Version) != expected && !IgnoresConditions))
            {
                return (ObjectVersion?)null;
            }

            _objects[name.Value] = (content.ToArray(), ++_versions);
            return Version(_versions);
        });

    public ValueTask<ObjectRead?> ReadAsync(ObjectName name, ByteOffset offset, ByteCount length, CancellationToken cancellationToken) =>
        Run(ObjectOperation.Read, name, () =>
        {
            if (!_objects.TryGetValue(name.Value, out var found))
            {
                return (ObjectRead?)null;
            }

            long start = Math.Min(offset.Value, found.Bytes.Length);
            int count = (int)Math.Min(found.Bytes.Length - start, length.Value);
            return new ObjectRead(found.Bytes.AsSpan((int)start, count).ToArray(), Version(found.Version), new ByteCount(found.Bytes.Length));
        });

    public ValueTask<IReadOnlyList<ObjectEntry>> ListAsync(ObjectName prefix, CancellationToken cancellationToken) =>
        Run(ObjectOperation.List, prefix, () =>
        {
            List<ObjectEntry> entries = [];

            foreach ((string name, var found) in _objects)
            {
                if (name.StartsWith(prefix.Value, StringComparison.Ordinal))
                {
                    entries.Add(new ObjectEntry(new ObjectName(name), new ByteCount(found.Bytes.Length), Version(found.Version)));
                }
            }

            return (IReadOnlyList<ObjectEntry>)entries;
        });

    public ValueTask<bool> DeleteAsync(ObjectName name, CancellationToken cancellationToken) =>
        Run(ObjectOperation.Delete, name, () => _objects.Remove(name.Value));

    /// <summary>How a message names this store.</summary>
    public override string ToString() => "memory://objects";

    private static ObjectVersion Version(long version) => new("v" + version.ToString(CultureInfo.InvariantCulture));

    // The condition is checked, and the write made a moment later without
    // checking again: every request that checked in that moment writes.
    private async ValueTask<ObjectVersion?> Racing(ObjectOperation operation, ObjectName name, Func<bool> condition, ReadOnlyMemory<byte> content)
    {
        if (!await Run(operation, name, condition))
        {
            return null;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(20));

        lock (_gate)
        {
            _objects[name.Value] = (content.ToArray(), ++_versions);
            return Version(_versions);
        }
    }

    private ValueTask<TResult> Run<TResult>(ObjectOperation operation, ObjectName name, Func<TResult> apply)
    {
        Action? after;

        lock (_gate)
        {
            _counts[operation] = _counts.GetValueOrDefault(operation) + 1;
        }

        after = Hook?.Invoke(operation, name);
        TResult result;

        lock (_gate)
        {
            result = apply();
        }

        after?.Invoke();
        return new ValueTask<TResult>(result);
    }
}

/// <summary>A writer's probe key's random part, for the tests (ADR 0134).</summary>
internal sealed class TestRandomness : Varve.Store.ObjectStorage.Model.IProbeRandomness
{
    public static TestRandomness Instance { get; } = new();

    public void Fill(Span<byte> destination) => Random.Shared.NextBytes(destination);
}
