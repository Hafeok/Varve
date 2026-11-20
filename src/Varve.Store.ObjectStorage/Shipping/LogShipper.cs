// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// Ships one dataset's log to one target (ADR 0184): every chunk and every
/// consolidated segment the source holds, created on the target with the
/// source's bytes, in slot order. It only reads the source. On the target it
/// creates, and deletes the chunks a consolidated segment replaced; it never
/// overwrites.
/// </summary>
/// <remarks>
/// <para>
/// A shipper holds what it needs to go on in memory and nothing on disk but
/// a segment on its way: started again, it reads where it was from the
/// target's own tail. Two shippers of one source to one target write the
/// same bytes, so either's create is the other's success.
/// </para>
/// <para>
/// It never opens the target as a dataset, and it claims nothing on either
/// store.
/// </para>
/// </remarks>
[Contract(typeof(LogShipping.NothingOnATargetIsOverwritten), Role = "the shipper of one dataset's log to one target")]
public sealed class LogShipper
{
    private const int ReadWindow = 4 << 20;

    // The catalog's name under derived/, and the bytes of its wrapper, "VRVK" and the fencing epoch (ADR 0139).
    private const string CatalogBlob = "catalog";
    private const int CatalogWrapper = 12;

    private readonly ShippingOptions _options;
    private readonly ObjectNames _source;
    private readonly ObjectNames _target;

    // The target's segments that have chunks and no consolidated object yet, in order.
    private readonly List<Held> _pending = [];
    private int _nextSegment;

    // The catalog version the shipper's reader record on the source announces.
    private CatalogVersion? _announced;

    private LogShipper(ShippingOptions options)
    {
        _options = options;
        _source = new ObjectNames(options.SourcePrefix);
        _target = new ObjectNames(options.TargetPrefix);
    }

    /// <summary>
    /// Starts shipping to a target, or resumes it: writes the lineage record
    /// and the manifest of a new target, and reads how far an existing one
    /// got from its own log. Null when the source holds no dataset yet.
    /// </summary>
    /// <exception cref="ShippingTargetNotEmptyException">The target's prefix holds something that is not a replica of this source.</exception>
    /// <exception cref="ReplicaPromotedException">The target was promoted.</exception>
    /// <exception cref="ShippingDivergedException">The target's tail is not the source's.</exception>
    public static async ValueTask<LogShipper?> StartAsync(ShippingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Source);
        ArgumentNullException.ThrowIfNull(options.Target);
        ArgumentNullException.ThrowIfNull(options.Lineage);
        ArgumentNullException.ThrowIfNull(options.Clock);

        if (options.SourcePrefix is { IsPrefix: false } || options.TargetPrefix is { IsPrefix: false })
        {
            throw new ArgumentException("A prefix ends in '/'.", nameof(options));
        }

        LogShipper shipper = new(options);
        Directory.CreateDirectory(options.Staging.Value);

        if (await ConditionalWrites.ReadWholeAsync(options.Source, shipper._source.Manifest, cancellationToken).ConfigureAwait(false) is not { } manifest)
        {
            return null;
        }

        IReadOnlyList<ObjectEntry> listed = await options.Target.ListAsync(shipper._target.Log, cancellationToken).ConfigureAwait(false);
        await shipper.ClaimTargetAsync(listed.Count > 0, cancellationToken).ConfigureAwait(false);

        (CreateOutcome outcome, _) = await ConditionalWrites.CreateAsync(options.Target, shipper._target.Manifest, manifest.Bytes, options.Clock, cancellationToken).ConfigureAwait(false);

        if (outcome == CreateOutcome.Taken)
        {
            throw new ShippingTargetNotEmptyException("The shipping target's manifest names another dataset than the source's. Nothing was written to its log (ADR 0184).");
        }

        await shipper.ResumeAsync(listed, cancellationToken).ConfigureAwait(false);
        return shipper;
    }

    /// <summary>
    /// One round: ships every log object the source has that the target
    /// lacks, in slot order, and consolidates on the target what the source
    /// has consolidated. Returns when the source has nothing more.
    /// </summary>
    /// <exception cref="ReplicaPromotedException">The target was promoted.</exception>
    /// <exception cref="ShippingDivergedException">The target holds other bytes at a name the source's were due.</exception>
    /// <exception cref="LogVerificationException">A chunk of the source does not verify or does not follow.</exception>
    public async ValueTask<ShippedLog> ShipLogAsync(CancellationToken cancellationToken = default)
    {
        // The returning primary's first refusal (ADR 0185): read every round.
        if (await ObjectStorage.ReadLineageAsync(_options.Target, _options.TargetPrefix, cancellationToken).ConfigureAwait(false) is { Understood: true, State: LineageState.Promoted } promoted)
        {
            throw new ReplicaPromotedException(promoted);
        }

        bool moved = false;

        while (true)
        {
            // What the source has consolidated, the target consolidates.
            foreach (Held waiting in _pending.Where(held => held.Sealed || held.Partial).ToArray())
            {
                if (await ExistsAsync(_options.Source, _source.Segment(waiting.Segment), cancellationToken).ConfigureAwait(false))
                {
                    await ShipConsolidatedAsync(waiting, cancellationToken).ConfigureAwait(false);
                    moved = true;
                }
                else if (waiting.Partial)
                {
                    throw new ShippingDivergedException(new LogSlot(ObjectNames.Slot(waiting.Segment, 0)), "the target holds chunks of a segment that do not start at its first, and the source has not consolidated that segment");
                }
            }

            Held? open = _pending.LastOrDefault(held => !held.Sealed && !held.Partial);
            int segment = open?.Segment ?? _nextSegment;
            int chunk = open?.NextChunk ?? 0;

            if (await ConditionalWrites.ReadWholeAsync(_options.Source, _source.Chunk(segment, chunk), cancellationToken).ConfigureAwait(false) is { } read)
            {
                if (open is null)
                {
                    open = new Held(segment);
                    _pending.Add(open);
                    _nextSegment = segment + 1;
                }

                byte[] bytes = read.Bytes.ToArray();
                Follow(open, bytes);
                await CreateAsync(_target.Chunk(segment, chunk), bytes, new LogSlot(ObjectNames.Slot(segment, chunk)), cancellationToken).ConfigureAwait(false);
                moved = true;
                continue;
            }

            if (!await ExistsAsync(_options.Source, _source.Segment(segment), cancellationToken).ConfigureAwait(false))
            {
                return new ShippedLog { NextSlot = new LogSlot(ObjectNames.Slot(segment, chunk)), Moved = moved };
            }

            // The source consolidated it before these chunks were copied.
            if (open is null)
            {
                open = new Held(segment);
                _pending.Add(open);
                _nextSegment = segment + 1;
            }

            await ShipConsolidatedAsync(open, cancellationToken).ConfigureAwait(false);
            moved = true;
        }
    }

    /// <summary>
    /// One whole round (ADR 0184 decision 4): the log, then the extent sets
    /// the source's catalog names, then the catalog, last. The catalog is read
    /// and announced before the log is shipped, so every slot its cursor names
    /// is on the target before it is, and every set it names is there too.
    /// </summary>
    /// <exception cref="ReplicaPromotedException">The target was promoted.</exception>
    /// <exception cref="ShippingDivergedException">The target holds other bytes at a name the source's were due.</exception>
    public async ValueTask<ShippedRound> ShipAsync(CancellationToken cancellationToken = default)
    {
        (byte[] Bytes, CatalogSummary Summary)? catalog = await HoldCatalogAsync(cancellationToken).ConfigureAwait(false);
        ShippedLog log = await ShipLogAsync(cancellationToken).ConfigureAwait(false);

        if (catalog is not { } held)
        {
            return new ShippedRound { Log = log, Catalog = await TargetCatalogAsync(cancellationToken).ConfigureAwait(false), Moved = log.Moved };
        }

        bool moved = log.Moved;

        foreach (CatalogSet set in held.Summary.Sets)
        {
            foreach (BlobName blob in set.Blobs)
            {
                moved |= await ShipBlobAsync(blob, cancellationToken).ConfigureAwait(false);
            }
        }

        moved |= await PublishCatalogAsync(held.Bytes, held.Summary, cancellationToken).ConfigureAwait(false);
        return new ShippedRound { Log = log, Catalog = await TargetCatalogAsync(cancellationToken).ConfigureAwait(false), Moved = moved };
    }

    // The source's catalog, held: the shipper announces the version in its
    // reader record and reads the catalog again, until what it announced is
    // what the catalog is (ADR 0160, announce then confirm). Null when the
    // source has no catalog yet.
    private async ValueTask<(byte[] Bytes, CatalogSummary Summary)?> HoldCatalogAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (await ReadCatalogAsync(_options.Source, _source, cancellationToken).ConfigureAwait(false) is not { } read)
            {
                return null;
            }

            await AnnounceAsync(read.Summary.Version, cancellationToken).ConfigureAwait(false);

            if (await ReadCatalogAsync(_options.Source, _source, cancellationToken).ConfigureAwait(false) is { } again && again.Summary.Version == read.Summary.Version)
            {
                return read;
            }
        }

        throw new IOException("The source's catalog changed under eight attempts to announce the version being shipped.");
    }

    private static async ValueTask<(byte[] Bytes, CatalogSummary Summary)?> ReadCatalogAsync(IObjectStore store, ObjectNames names, CancellationToken cancellationToken)
    {
        if (await ConditionalWrites.ReadWholeAsync(store, names.Derived(CatalogBlob), cancellationToken).ConfigureAwait(false) is not { } read || read.Bytes.Length <= CatalogWrapper)
        {
            return null;
        }

        byte[] bytes = read.Bytes.ToArray();
        return CatalogSummary.TryRead(bytes.AsMemory(CatalogWrapper)) is { } summary ? (bytes, summary) : null;
    }

    private async ValueTask<CatalogSummary?> TargetCatalogAsync(CancellationToken cancellationToken) =>
        (await ReadCatalogAsync(_options.Target, _target, cancellationToken).ConfigureAwait(false))?.Summary;

    // The shipper's reader record on the source: created over whatever a
    // previous run of the same id left, and replaced against the version read.
    private async ValueTask AnnounceAsync(CatalogVersion version, CancellationToken cancellationToken)
    {
        if (_announced == version)
        {
            return;
        }

        ObjectName name = _source.Reader(_options.Reader);
        byte[] bytes = ReaderRecordFormat.Write(new ReaderRecord
        {
            Reader = _options.Reader,
            Holder = _options.Holder,
            Oldest = version,
            Newest = version,
            AnnouncedAt = new ClaimTime(_options.Clock.GetUtcNow()),
            MaxDerived = DerivedVersion.ThisBuildReads,
        });

        for (int attempt = 0; attempt < 8; attempt++)
        {
            ObjectRead? current = await ConditionalWrites.ReadWholeAsync(_options.Source, name, cancellationToken).ConfigureAwait(false);
            bool written = current is { } found
                ? await ConditionalWrites.ReplaceAsync(_options.Source, name, found.Version, bytes, _options.Clock, cancellationToken).ConfigureAwait(false) is not null
                : (await ConditionalWrites.CreateAsync(_options.Source, name, bytes, _options.Clock, cancellationToken).ConfigureAwait(false)).Outcome == CreateOutcome.Ours;

            if (written)
            {
                _announced = version;
                return;
            }
        }

        throw new IOException("The shipper's reader record '" + name + "' changed under every attempt to write it: another shipper announces under the reader id '" + _options.Reader + "'. Give each target its own.");
    }

    // One blob of a set, when the source has it and the target does not: the
    // source's bytes, by create-if-absent. Optional blobs a set lacks are skipped.
    private async ValueTask<bool> ShipBlobAsync(BlobName blob, CancellationToken cancellationToken)
    {
        ObjectName from = _source.Derived(blob.Value);
        ObjectName to = _target.Derived(blob.Value);

        if (await _options.Source.ReadAsync(from, new ByteOffset(0), new ByteCount(1), cancellationToken).ConfigureAwait(false) is not { } theirs)
        {
            return false;
        }

        if (await _options.Target.ReadAsync(to, new ByteOffset(0), new ByteCount(1), cancellationToken).ConfigureAwait(false) is { } ours)
        {
            // A set is never rewritten under its name (ADR 0158), so a blob of
            // the same name and another length is not the source's.
            return ours.Length == theirs.Length
                ? false
                : throw new ShippingDivergedException("The shipping target's '" + to + "' is not the length of the source's. Nothing was overwritten (ADR 0184); ship to a fresh prefix.");
        }

        string staged = Path.Combine(_options.Staging.Value, "blob");

        try
        {
            await DownloadAsync(from, staged, cancellationToken).ConfigureAwait(false);

            if (await ConditionalWrites.CreateAsync(_options.Target, to, new ObjectContent(new FileInfo(staged)), _options.Clock, cancellationToken).ConfigureAwait(false) == CreateOutcome.Taken)
            {
                throw new ShippingDivergedException("The shipping target's '" + to + "' holds other bytes than the source's. Nothing was overwritten (ADR 0184); ship to a fresh prefix.");
            }

            return true;
        }
        finally
        {
            File.Delete(staged);
        }
    }

    // The catalog, last: the source's bytes, wrapper included, over an older
    // catalog of the target's or none. Never over a newer one, and never
    // while another shipper has moved it meanwhile.
    private async ValueTask<bool> PublishCatalogAsync(byte[] bytes, CatalogSummary summary, CancellationToken cancellationToken)
    {
        ObjectName name = _target.Derived(CatalogBlob);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            ObjectRead? current = await ConditionalWrites.ReadWholeAsync(_options.Target, name, cancellationToken).ConfigureAwait(false);

            if (current is null)
            {
                if ((await ConditionalWrites.CreateAsync(_options.Target, name, bytes, _options.Clock, cancellationToken).ConfigureAwait(false)).Outcome == CreateOutcome.Ours)
                {
                    return true;
                }

                continue;
            }

            if (current.Bytes.Span.SequenceEqual(bytes))
            {
                return false;
            }

            if (current.Bytes.Length > CatalogWrapper && CatalogSummary.TryRead(current.Bytes[CatalogWrapper..]) is { } theirs && theirs.Version.Value >= summary.Version.Value)
            {
                // Another shipper put a newer one there, or the same version in other bytes.
                return theirs.Version.Value > summary.Version.Value
                    ? false
                    : throw new ShippingDivergedException("The shipping target's catalog is version " + theirs.Version + " as the source's is, in other bytes. Nothing was overwritten (ADR 0184); ship to a fresh prefix.");
            }

            if (await ConditionalWrites.ReplaceAsync(_options.Target, name, current.Version, bytes, _options.Clock, cancellationToken).ConfigureAwait(false) is not null)
            {
                return true;
            }
        }

        throw new IOException("The shipping target's catalog changed under four attempts to replace it.");
    }

    // A new target gets its lineage record before anything else; one that has
    // a record must be a replica of this source.
    private async ValueTask ClaimTargetAsync(bool hasLog, CancellationToken cancellationToken)
    {
        LineageRecord ours = new() { Understood = true, State = LineageState.Replica, Source = _options.Lineage };

        if (await ObjectStorage.ReadLineageAsync(_options.Target, _options.TargetPrefix, cancellationToken).ConfigureAwait(false) is not { } lineage)
        {
            if (hasLog || await ExistsAsync(_options.Target, _target.Writer, cancellationToken).ConfigureAwait(false))
            {
                throw new ShippingTargetNotEmptyException("The shipping target's prefix holds a dataset and no lineage record, so it is not a replica of this source: a dataset of its own, or a copy Dataset.ShipAsync made. Nothing was written to it. Ship to an empty prefix (ADR 0184).");
            }

            (CreateOutcome outcome, _) = await ConditionalWrites.CreateAsync(_options.Target, _target.Lineage, LineageRecordFormat.Write(ours), _options.Clock, cancellationToken).ConfigureAwait(false);

            if (outcome == CreateOutcome.Ours)
            {
                return;
            }

            // Another shipper's record, written since: judged like any other.
            lineage = await ObjectStorage.ReadLineageAsync(_options.Target, _options.TargetPrefix, cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("The shipping target's lineage record was there and is gone.");
        }

        if (lineage is { Understood: true, State: LineageState.Promoted })
        {
            throw new ReplicaPromotedException(lineage);
        }

        if (!lineage.Understood || lineage.State != LineageState.Replica || lineage.Source != _options.Lineage)
        {
            throw new ShippingTargetNotEmptyException(lineage.Understood
                ? "The shipping target is a copy of another dataset: its lineage record names " + lineage.Source!.Store + " and dataset " + lineage.Source.Dataset + ". Nothing was written to it (ADR 0184)."
                : "The shipping target's lineage record is of a format this version of Varve does not read, so nothing is written on it (ADRs 0184, 0217). Upgrade Varve.");
        }
    }

    // Where the target got to, read from its own log: the listing, the chunks
    // of each segment it has not consolidated, and its tail against the source's.
    private async ValueTask ResumeAsync(IReadOnlyList<ObjectEntry> listed, CancellationToken cancellationToken)
    {
        SortedDictionary<int, (bool Consolidated, SortedSet<int> Chunks)> segments = [];

        foreach (ObjectEntry entry in listed)
        {
            if (!_target.TryParse(entry.Name, out int segment, out int chunk))
            {
                continue;
            }

            (bool consolidated, SortedSet<int> chunks) = segments.TryGetValue(segment, out var found) ? found : (false, []);

            if (chunk < 0)
            {
                consolidated = true;
            }
            else
            {
                chunks.Add(chunk);
            }

            segments[segment] = (consolidated, chunks);
        }

        _nextSegment = segments.Count == 0 ? 0 : segments.Keys.Max() + 1;

        foreach ((int segment, (bool consolidated, SortedSet<int> chunks)) in segments)
        {
            Held held = new(segment);
            bool contiguous = chunks.Count > 0 && chunks.Min == 0 && chunks.Max == chunks.Count - 1;

            foreach (int chunk in chunks)
            {
                byte[] bytes = (await ConditionalWrites.ReadWholeAsync(_options.Target, _target.Chunk(segment, chunk), cancellationToken).ConfigureAwait(false))?.Bytes.ToArray()
                    ?? throw new IOException("The shipping target listed '" + _target.Chunk(segment, chunk) + "' and no longer has it: something else is deleting under a replica's prefix.");

                try
                {
                    if (consolidated || !contiguous)
                    {
                        held.Remember(bytes, new LogSlot(ObjectNames.Slot(segment, chunk)));
                    }
                    else
                    {
                        Follow(held, bytes);
                    }
                }
                catch (LogVerificationException broken)
                {
                    // The target's own chunks: what does not verify there is not the source's.
                    throw new ShippingDivergedException(new LogSlot(ObjectNames.Slot(segment, chunk)), "the target's chunk does not verify, or does not follow the one before it", broken);
                }
            }

            if (consolidated)
            {
                // A consolidation the last shipper did not finish: only the deletes were left.
                await DeleteReplacedAsync(held, cancellationToken).ConfigureAwait(false);
                continue;
            }

            held.Partial = !contiguous;
            _pending.Add(held);
        }

        if (_pending.SkipLast(1).Any(held => !held.Sealed && !held.Partial))
        {
            Held broken = _pending.First(held => !held.Sealed && !held.Partial);
            throw new ShippingDivergedException(new LogSlot(ObjectNames.Slot(broken.Segment, broken.NextChunk)), "the target has a later segment after one that was never sealed");
        }

        // The tail, against the source at the same slot.
        if (_pending.LastOrDefault(held => !held.Partial) is { NextChunk: > 0 } tail)
        {
            int last = tail.NextChunk - 1;
            LogSlot slot = new(ObjectNames.Slot(tail.Segment, last));
            ObjectRead? theirs = await ConditionalWrites.ReadWholeAsync(_options.Source, _source.Chunk(tail.Segment, last), cancellationToken).ConfigureAwait(false);
            ObjectRead? ours = await ConditionalWrites.ReadWholeAsync(_options.Target, _target.Chunk(tail.Segment, last), cancellationToken).ConfigureAwait(false);

            if (theirs is { } source)
            {
                if (ours is null || !source.Bytes.Span.SequenceEqual(ours.Bytes.Span))
                {
                    throw new ShippingDivergedException(slot, "the target's last chunk is not the source's");
                }
            }
            else if (!await ExistsAsync(_options.Source, _source.Segment(tail.Segment), cancellationToken).ConfigureAwait(false))
            {
                throw new ShippingDivergedException(slot, "the target has a chunk the source has neither as a chunk nor in a consolidated segment");
            }
        }
    }

    // A consolidated segment of the source, onto the target: its fence chunks,
    // then the segment, then the deletes of the chunks it replaced. Never a
    // delete before the segment is there.
    private async ValueTask ShipConsolidatedAsync(Held held, CancellationToken cancellationToken)
    {
        string staged = Path.Combine(_options.Staging.Value, held.Segment.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + ".seg");

        try
        {
            await DownloadAsync(_source.Segment(held.Segment), staged, cancellationToken).ConfigureAwait(false);

            // What the target has of it must be a prefix of it.
            using (FileStream file = new(staged, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess))
            {
                foreach (Shipped chunk in held.Chunks.Where(chunk => chunk.Length > 0))
                {
                    if (await HashOfAsync(file, chunk.Offset, chunk.Length, cancellationToken).ConfigureAwait(false) is not { } hash || !hash.AsSpan().SequenceEqual(chunk.Hash))
                    {
                        throw new ShippingDivergedException(chunk.Slot, "the target's chunk is not what the source's consolidated segment holds at its offset");
                    }
                }
            }

            // The fence chunks the source kept, which the target does not have yet.
            HashSet<int> have = [.. held.Chunks.Select(chunk => chunk.Chunk)];

            foreach (ObjectEntry entry in await _options.Source.ListAsync(_source.Chunks(held.Segment), cancellationToken).ConfigureAwait(false))
            {
                if (!_source.TryParse(entry.Name, out int segment, out int chunk) || segment != held.Segment || chunk < 0 || have.Contains(chunk))
                {
                    continue;
                }

                // Listed and then gone is a data chunk the source was still deleting.
                if (await ConditionalWrites.ReadWholeAsync(_options.Source, entry.Name, cancellationToken).ConfigureAwait(false) is not { } kept)
                {
                    continue;
                }

                byte[] bytes = kept.Bytes.ToArray();
                LogSlot slot = new(ObjectNames.Slot(segment, chunk));

                if (!ChunkHeader.TryRead(bytes, out ChunkHeader header, out _))
                {
                    throw new LogVerificationException("The source's chunk at log slot " + slot + " does not verify.");
                }

                if ((header.Flags & ChunkFlags.Fence) == 0)
                {
                    continue;
                }

                await CreateAsync(_target.Chunk(segment, chunk), bytes, slot, cancellationToken).ConfigureAwait(false);
                held.Remember(bytes, slot);
            }

            if (await ConditionalWrites.CreateAsync(_options.Target, _target.Segment(held.Segment), new ObjectContent(new FileInfo(staged)), _options.Clock, cancellationToken).ConfigureAwait(false) == CreateOutcome.Taken)
            {
                throw new ShippingDivergedException(new LogSlot(ObjectNames.Slot(held.Segment, 0)), "the target's consolidated segment is not the source's");
            }

            await DeleteReplacedAsync(held, cancellationToken).ConfigureAwait(false);
            _pending.Remove(held);
        }
        finally
        {
            File.Delete(staged);
        }
    }

    // The target's chunks a consolidated segment replaced, except its fence chunks, which are kept for ever (ADR 0136).
    private async ValueTask DeleteReplacedAsync(Held held, CancellationToken cancellationToken)
    {
        foreach (Shipped chunk in held.Chunks.Where(chunk => !chunk.Fence))
        {
            await _options.Target.DeleteAsync(_target.Chunk(held.Segment, chunk.Chunk), cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask CreateAsync(ObjectName name, byte[] bytes, LogSlot slot, CancellationToken cancellationToken)
    {
        (CreateOutcome outcome, _) = await ConditionalWrites.CreateAsync(_options.Target, name, bytes, _options.Clock, cancellationToken).ConfigureAwait(false);

        if (outcome == CreateOutcome.Taken)
        {
            throw new ShippingDivergedException(slot, "the target holds other bytes there than the source's");
        }
    }

    private async ValueTask DownloadAsync(ObjectName name, string path, CancellationToken cancellationToken)
    {
        FileStream file = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.SequentialScan);

        await using (file.ConfigureAwait(false))
        {
            long offset = 0;

            while (true)
            {
                ObjectRead read = await _options.Source.ReadAsync(name, new ByteOffset(offset), new ByteCount(ReadWindow), cancellationToken).ConfigureAwait(false)
                    ?? throw new IOException("The source's '" + name + "' was there and is gone.");
                await file.WriteAsync(read.Bytes, cancellationToken).ConfigureAwait(false);
                offset += read.Bytes.Length;

                if (offset >= read.Length.Value || read.Bytes.IsEmpty)
                {
                    break;
                }
            }
        }
    }

    private static async ValueTask<byte[]?> HashOfAsync(FileStream file, long offset, int length, CancellationToken cancellationToken)
    {
        if (offset + length > file.Length)
        {
            return null;
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[Math.Min(length, 1 << 16)];
        file.Position = offset;
        int left = length;

        while (left > 0)
        {
            int read = await file.ReadAsync(buffer.AsMemory(0, Math.Min(left, buffer.Length)), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                return null;
            }

            hash.AppendData(buffer, 0, read);
            left -= read;
        }

        return hash.GetHashAndReset();
    }

    private static async ValueTask<bool> ExistsAsync(IObjectStore store, ObjectName name, CancellationToken cancellationToken) =>
        await store.ReadAsync(name, new ByteOffset(0), new ByteCount(1), cancellationToken).ConfigureAwait(false) is not null;

    // A chunk that continues a segment: its header and bytes verify, it is the
    // next of that segment, and it chains to the one before (ADR 0135).
    private static void Follow(Held held, byte[] bytes)
    {
        LogSlot slot = new(ObjectNames.Slot(held.Segment, held.NextChunk));

        if (!ChunkHeader.TryRead(bytes, out ChunkHeader header, out byte[] hash))
        {
            throw new LogVerificationException("The chunk at log slot " + slot + " does not verify.");
        }

        if (header.Segment != held.Segment || header.Chunk != held.NextChunk || header.Offset != held.Length || !header.Previous.AsSpan().SequenceEqual(held.Previous))
        {
            throw new LogVerificationException("The chunk at log slot " + slot + " does not follow the chunk before it.");
        }

        held.Chunks.Add(new Shipped(header.Chunk, (header.Flags & ChunkFlags.Fence) != 0, header.Offset, header.Length, header.PayloadHash, slot));
        held.NextChunk = header.Chunk + 1;
        held.Length = header.Offset + header.Length;
        held.Previous = hash;
        held.Sealed |= (header.Flags & ChunkFlags.Seal) != 0;
    }

    private sealed record Shipped(int Chunk, bool Fence, long Offset, int Length, byte[] Hash, LogSlot Slot);

    // A segment of the target that is still chunks.
    private sealed class Held(int segment)
    {
        public int Segment { get; } = segment;

        public List<Shipped> Chunks { get; } = [];

        public int NextChunk { get; set; }

        public long Length { get; set; }

        public byte[] Previous { get; set; } = ChunkHeader.Nothing();

        public bool Sealed { get; set; }

        // The target has chunks of it that do not start at its first: the fence
        // chunks of a segment the source consolidated before it was shipped.
        public bool Partial { get; set; }

        // A chunk the target has that is not followed as part of a chain.
        public void Remember(byte[] bytes, LogSlot slot)
        {
            if (!ChunkHeader.TryRead(bytes, out ChunkHeader header, out _))
            {
                throw new LogVerificationException("The chunk at log slot " + slot + " does not verify.");
            }

            Chunks.Add(new Shipped(header.Chunk, (header.Flags & ChunkFlags.Fence) != 0, header.Offset, header.Length, header.PayloadHash, slot));
        }
    }
}
