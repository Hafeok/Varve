// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Store;

/// <summary>
/// Storage held in memory: a real backend whose durability is
/// <see cref="Durability.None"/>, stated rather than implied.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0040. It is the embedded case when nothing is persisted, and what the
/// specification's §10 properties run against. It is not a test double: it
/// keeps the same bytes a durable backend would, so the header chain, recovery
/// and determinism are tested against something.
/// </para>
/// <para>
/// A segment grows by copying into a larger buffer. A slice handed out earlier
/// keeps pointing into the old buffer, whose written bytes are never touched
/// again — so a slice is immutable and may be held, as the contract promises.
/// </para>
/// </remarks>
public sealed class MemoryStorage : IStorage
{
    private readonly MemorySegmentStore _log;
    private readonly MemoryDerivedStore _derived;

    /// <summary>Empty storage: no segments and no derived data.</summary>
    public MemoryStorage()
        : this([], [])
    {
    }

    private MemoryStorage(List<Segment> segments, SortedDictionary<string, ReadOnlyMemory<byte>> blobs)
    {
        _log = new MemorySegmentStore(segments);
        _derived = new MemoryDerivedStore(blobs);
    }

    /// <inheritdoc />
    public ISegmentStore Log => _log;

    /// <inheritdoc />
    public IDerivedStore Derived => _derived;

    /// <summary>
    /// Storage restored from bytes someone kept: the log's segments in order,
    /// and optionally derived blobs by name. Every segment but the last is
    /// sealed; the last is open, as it would be in a copy taken while the
    /// dataset was live.
    /// </summary>
    public static MemoryStorage FromSegments(
        IEnumerable<ReadOnlyMemory<byte>> log,
        IEnumerable<KeyValuePair<string, ReadOnlyMemory<byte>>>? derived = null)
    {
        ArgumentNullException.ThrowIfNull(log);

        List<Segment> segments = [];

        foreach (ReadOnlyMemory<byte> bytes in log)
        {
            if (segments.Count > 0)
            {
                segments[^1].Sealed = true;
            }

            Segment segment = new();
            segment.Append(bytes.Span);
            segments.Add(segment);
        }

        SortedDictionary<string, ReadOnlyMemory<byte>> blobs = new(StringComparer.Ordinal);

        if (derived is not null)
        {
            foreach (KeyValuePair<string, ReadOnlyMemory<byte>> blob in derived)
            {
                blobs[blob.Key] = blob.Value.ToArray();
            }
        }

        return new MemoryStorage(segments, blobs);
    }

    private sealed class Segment
    {
        private byte[] _buffer = [];

        public int Length { get; private set; }

        public bool Sealed { get; set; }

        public ReadOnlyMemory<byte> Written => _buffer.AsMemory(0, Length);

        public void Append(ReadOnlySpan<byte> bytes)
        {
            if (Length + bytes.Length > _buffer.Length)
            {
                // Never written into again once replaced: slices of it stay valid.
                byte[] larger = new byte[Math.Max(Length + bytes.Length, Math.Max(256, _buffer.Length * 2))];
                _buffer.AsSpan(0, Length).CopyTo(larger);
                _buffer = larger;
            }

            bytes.CopyTo(_buffer.AsSpan(Length));
            Length += bytes.Length;
        }
    }

    private sealed class MemorySegmentStore : ISegmentStore
    {
        private readonly List<Segment> _segments;
        private readonly Lock _gate = new();

        public MemorySegmentStore(List<Segment> segments) => _segments = segments;

        public Durability Durability => Durability.None;

        public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                SegmentInfo[] list = new SegmentInfo[_segments.Count];

                for (int i = 0; i < list.Length; i++)
                {
                    list[i] = new SegmentInfo(i, _segments[i].Length, _segments[i].Sealed);
                }

                return new ValueTask<IReadOnlyList<SegmentInfo>>(list);
            }
        }

        public ValueTask<int> CreateSegmentAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_segments.Count > 0 && !_segments[^1].Sealed)
                {
                    throw new InvalidOperationException("The newest segment is not sealed; only one segment may be open.");
                }

                _segments.Add(new Segment());
                return new ValueTask<int>(_segments.Count - 1);
            }
        }

        public ValueTask AppendAsync(int segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Segment target = Get(segment);

                if (target.Sealed)
                {
                    throw new InvalidOperationException("Segment " + segment + " is sealed and never changes again.");
                }

                target.Append(bytes.Span);
                return ValueTask.CompletedTask;
            }
        }

        public ValueTask FlushAsync(int segment, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Get(segment);
                return ValueTask.CompletedTask;
            }
        }

        public ValueTask SealAsync(int segment, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Get(segment).Sealed = true;
                return ValueTask.CompletedTask;
            }
        }

        public ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(int segment, long offset, int length, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                ReadOnlyMemory<byte> written = Get(segment).Written;
                return new ValueTask<ReadOnlyMemory<byte>>(Slice(written, offset, length));
            }
        }

        private Segment Get(int segment) =>
            segment >= 0 && segment < _segments.Count
                ? _segments[segment]
                : throw new ArgumentOutOfRangeException(nameof(segment), segment, "No such segment.");
    }

    private sealed class MemoryDerivedStore : IDerivedStore
    {
        private readonly SortedDictionary<string, ReadOnlyMemory<byte>> _blobs;
        private readonly Lock _gate = new();

        public MemoryDerivedStore(SortedDictionary<string, ReadOnlyMemory<byte>> blobs) => _blobs = blobs;

        public ValueTask PutAsync(string name, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            byte[] copy = bytes.ToArray();

            lock (_gate)
            {
                _blobs[name] = copy;
                return ValueTask.CompletedTask;
            }
        }

        public ValueTask<ReadOnlyMemory<byte>> GetRangeAsync(string name, long offset, int length, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!_blobs.TryGetValue(name, out ReadOnlyMemory<byte> blob))
                {
                    throw new KeyNotFoundException("No derived blob named '" + name + "'.");
                }

                return new ValueTask<ReadOnlyMemory<byte>>(Slice(blob, offset, length));
            }
        }

        public ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return new ValueTask<bool>(_blobs.Remove(name));
            }
        }

        public ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return new ValueTask<IReadOnlyList<string>>([.. _blobs.Keys]);
            }
        }
    }

    private static ReadOnlyMemory<byte> Slice(ReadOnlyMemory<byte> bytes, long offset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (offset >= bytes.Length)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        int start = (int)offset;
        return bytes.Slice(start, Math.Min(length, bytes.Length - start));
    }
}
