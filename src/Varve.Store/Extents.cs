// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System;
using DecisionDriven.Ledger.Varve;
using DecisionDriven;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// An extent set (ADR 0139): the default projection's intervals over one
/// window of positions, <c>(From, To]</c>, as one extent per order and a
/// dictionary extent, written once and never changed. Read as one run — the
/// window's exact delta against the sets before it — whose sections are the
/// order extents' and whose dictionary is the dictionary extent's.
/// </summary>
internal sealed class ExtentSet
{
    internal const string Prefix = "extents/";

    private ExtentSet(string name, LoadedRun[] orders, LoadedRun terms)
    {
        Name = name;
        Orders = orders;
        Terms = terms;
        KeySection[] asserted = new KeySection[Store.Orders.Count];
        KeySection[] retracted = new KeySection[Store.Orders.Count];

        for (int o = 0; o < asserted.Length; o++)
        {
            asserted[o] = orders[o].Run.Asserted((IndexOrder)o);
            retracted[o] = orders[o].Run.Retracted((IndexOrder)o);
        }

        Run = new Run(asserted, retracted, terms.Header.From, terms.Header.To, terms.Run.Terms, new RunBlob(new BlobName(name), new Parts(this)));
    }

    /// <summary>The set's name: the prefix of its extents' names, ending in a slash.</summary>
    internal string Name { get; }

    internal long From => Terms.Header.From;

    internal long To => Terms.Header.To;

    internal byte[] ToHash => Terms.Header.ToHash;

    internal long BlankCount => Terms.BlankCount;

    /// <summary>One extent per order, in <see cref="IndexOrder"/>'s order.</summary>
    internal LoadedRun[] Orders { get; }

    /// <summary>The window's dictionary entries: a run with no keys.</summary>
    internal LoadedRun Terms { get; }

    /// <summary>The set as a run, held open by the catalog that names it and every view that reads it.</summary>
    internal Run Run { get; }

    /// <summary>
    /// Whether a read inside the window seeks the set rather than narrowing
    /// it: every extent of it is format 5 (ADR 0177). A format-4 set is
    /// narrowed until maintenance rewrites it.
    /// </summary>
    internal bool Seekable => Array.TrueForAll(Orders, o => o.Extent!.Seekable);

    /// <summary>
    /// Whether every blob of the set is checked block by block (ADR 0213):
    /// it is seekable, and its dictionary, written in the same version as
    /// its commit index, is version 6. A set written while a reader of 5 or
    /// older was announced is not, until maintenance rewrites it.
    /// </summary>
    internal bool Checked => Seekable && Terms.Header.Version == DerivedFormat.CheckedVersion;

    /// <summary>The graph-version index at the set's end (ADR 0125), beside its extents.</summary>
    internal BlobName Graphs => GraphsOf(Name);

    internal static string NameOf(long from, long to, long sequence) =>
        Prefix + from.ToString("D20", CultureInfo.InvariantCulture) + "-" + to.ToString("D20", CultureInfo.InvariantCulture)
        + "." + sequence.ToString(CultureInfo.InvariantCulture) + "/";

    internal static BlobName OrderOf(string name, IndexOrder order) => new(name + "order" + ((int)order).ToString(CultureInfo.InvariantCulture));

    internal static BlobName TermsOf(string name) => new(name + "terms");

    internal static BlobName GraphsOf(string name) => new(name + "graphs");

    /// <summary>The commit index of the set's window (ADR 0161): optional, absent from a set written before it.</summary>
    internal static BlobName CommitsOf(string name) => new(name + "commits");

    /// <summary>The bytes of the set's extents: its six orders and its dictionary.</summary>
    internal long Bytes => Run.Blob!.Blob.Length.Value;

    /// <summary>The sequence the set's name carries.</summary>
    internal long Sequence => SequenceOf(Name);

    /// <summary>Every blob a set's name stands for, for deletion.</summary>
    internal static IEnumerable<BlobName> BlobsOf(string name)
    {
        for (int o = 0; o < Store.Orders.Count; o++)
        {
            yield return OrderOf(name, (IndexOrder)o);
        }

        yield return TermsOf(name);
        yield return GraphsOf(name);
        yield return CommitsOf(name);
    }

    /// <summary>The sequence a set's name carries, or -1 when the name is not a set's.</summary>
    internal static long SequenceOf(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return -1;
        }

        int slash = name.IndexOf('/', Prefix.Length);
        string set = slash < 0 ? name : name[..(slash + 1)];
        int dot = set.LastIndexOf('.');

        return set.StartsWith(Prefix, StringComparison.Ordinal) && dot > 0 && set.EndsWith('/')
            && long.TryParse(set.AsSpan(dot + 1, set.Length - dot - 2), NumberStyles.None, CultureInfo.InvariantCulture, out long sequence)
            ? sequence
            : -1;
    }

    /// <summary>
    /// The set a catalog entry names, when every extent of it loads, is this
    /// dataset's, and covers the entry's window; null otherwise, which is a
    /// cache miss like any derived file's (ADR 0072).
    /// </summary>
    internal static async ValueTask<ExtentSet?> TryLoadAsync(IDerivedStore store, string name, DatasetId dataset, long from, long to, CancellationToken cancellationToken)
    {
        List<LoadedRun> loaded = [];

        try
        {
            for (int o = 0; o < Store.Orders.Count; o++)
            {
                LoadedRun? extent = await DerivedFormat.TryLoadAsync(store, OrderOf(name, (IndexOrder)o), dataset, DerivedFormat.KindExtent, cancellationToken).ConfigureAwait(false);

                if (extent is null)
                {
                    return null;
                }

                loaded.Add(extent);

                if (extent.Extent!.Order != (IndexOrder)o || extent.Header.From != from || extent.Header.To != to)
                {
                    return null;
                }
            }

            LoadedRun? terms = await DerivedFormat.TryLoadAsync(store, TermsOf(name), dataset, DerivedFormat.KindExtentTerms, cancellationToken, shared: true).ConfigureAwait(false);

            if (terms is null)
            {
                return null;
            }

            loaded.Add(terms);

            if (terms.Header.From != from || terms.Header.To != to || terms.Run.Count != 0)
            {
                return null;
            }

            foreach (LoadedRun extent in loaded)
            {
                if (!extent.Header.ToHash.AsSpan().SequenceEqual(terms.Header.ToHash))
                {
                    return null;
                }
            }

            ExtentSet set = new(name, [.. loaded.GetRange(0, Store.Orders.Count)], terms);
            loaded.Clear();
            return set;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
        finally
        {
            foreach (LoadedRun extent in loaded)
            {
                extent.Run.Blob!.Release();
            }
        }
    }

    /// <summary>
    /// Writes a set from its rows (ADR 0139): each order's extent from that
    /// order's rows by section, then the dictionary extent from the window's
    /// term sections. Nothing names the set until a catalog does.
    /// </summary>
    [DesignDecision(typeof(SeekingExtentSets.EveryWriterWritesFormat5), Scope = ExceptionScope.Boundary)]
    internal static async ValueTask WriteAsync(
        IDerivedStore store,
        string name,
        DatasetId dataset,
        long from,
        long to,
        byte[] toHash,
        long blankCount,
        Func<IndexOrder, long, CancellationToken, ValueTask<IRowSource>> rows,
        TermSection[] terms,
        CancellationToken cancellationToken,
        ushort version = DerivedFormat.ExtentVersion,
        ushort termsVersion = DerivedFormat.CheckedVersion)
    {
        for (int o = 0; o < Store.Orders.Count; o++)
        {
            IndexOrder order = (IndexOrder)o;
            await DerivedFormat.WriteExtentAsync(store, OrderOf(name, order), order, dataset, from, to, toHash, (section, token) => rows(order, section, token), cancellationToken, version).ConfigureAwait(false);
        }

        await DerivedFormat.WriteRunAsync(
            store, TermsOf(name), DerivedFormat.KindExtentTerms, dataset, from, to, toHash,
            (_, _) => new ValueTask<IKeySource>(new SectionSource(KeySection.Empty)), terms, blankCount, cancellationToken, termsVersion).ConfigureAwait(false);
    }

    /// <summary>The rows of one order and section, read from the set's extent.</summary>
    internal IRowSource Rows(IndexOrder order) => new ExtentRows(Orders[(int)order]);

    /// <summary>
    /// The set's extents as one blob, for the run's lifetime: their bytes one
    /// after another, in the order of their names, and disposing it closes
    /// them all.
    /// </summary>
    private sealed class Parts : IReadableBlob
    {
        private readonly ExtentSet _set;
        private readonly IReadableBlob[] _parts;
        private readonly long[] _lengths;

        internal Parts(ExtentSet set)
        {
            _set = set;
            _parts = new IReadableBlob[set.Orders.Length + 1];
            _lengths = new long[_parts.Length];

            for (int i = 0; i < _parts.Length; i++)
            {
                _parts[i] = i < set.Orders.Length ? set.Orders[i].Run.Blob!.Blob : set.Terms.Run.Blob!.Blob;
                _lengths[i] = _parts[i].Length.Value;
                Total += _lengths[i];
            }
        }

        private long Total { get; }

        public ByteCount Length => new(Total);

        public int Read(ByteOffset offset, Span<byte> destination)
        {
            long at = offset.Value;
            int read = 0;

            for (int i = 0; i < _parts.Length && read < destination.Length; i++)
            {
                if (at >= _lengths[i])
                {
                    at -= _lengths[i];
                    continue;
                }

                read += _parts[i].Read(new ByteOffset(at), destination[read..]);
                at = 0;
            }

            return read;
        }

        public void Dispose()
        {
            foreach (LoadedRun extent in _set.Orders)
            {
                extent.Run.Blob!.Release();
            }

            _set.Terms.Run.Blob!.Release();
        }
    }
}

/// <summary>An extent set as the catalog names it.</summary>
internal readonly record struct CatalogEntry(string Name, long From, long To);

/// <summary>A commit index entry the catalog carries, and where the log continues after it.</summary>
internal readonly record struct CatalogPin(long Position, CommitEntry Entry, int EndSegment, long EndOffset);

/// <summary>
/// Where the log continues after the catalog's position, and what an open
/// that begins there needs of the commit index below it (ADR 0139): the
/// segment and offset where the next commit's records start, the backend's
/// slot for that byte, the entries of the sets' ends, and the settings
/// commits up to the position.
/// </summary>
internal sealed record CatalogCursor(int Segment, long Offset, long Slot, CatalogPin[] Pins, SettingsPoint[] Settings);

/// <summary>
/// The projection catalog (ADR 0139): the extent sets that make the default
/// projection up to its position, oldest first, each a window that starts
/// where the one before it ends, and the sequence the next set's name takes.
/// One blob, <c>catalog</c>, replaced whole: atomically on a directory, and
/// on an object store by a conditional replace under the writer's epoch, so a
/// superseded writer cannot swap it (ADR 0136).
/// </summary>
internal static class ExtentCatalog
{
    internal static readonly BlobName Name = new("catalog");

    internal static async ValueTask WriteAsync(
        IDerivedStore store, DatasetId dataset, long position, byte[] positionHash, long sequence, IReadOnlyList<CatalogEntry> sets, CatalogCursor? cursor, CancellationToken cancellationToken)
    {
        ArrayBufferWriter<byte> body = new();
        LogFormat.WriteUInt64(body, (ulong)sequence);
        LogFormat.WriteUInt32(body, (uint)sets.Count);

        foreach (CatalogEntry set in sets)
        {
            byte[] name = Encoding.UTF8.GetBytes(set.Name);
            BinaryPrimitives.WriteUInt16LittleEndian(body.GetSpan(2), (ushort)name.Length);
            body.Advance(2);
            body.Write(name);
            LogFormat.WriteUInt64(body, (ulong)set.From);
            LogFormat.WriteUInt64(body, (ulong)set.To);
        }

        // The cursor (ADR 0139, CatalogRecordsTheLogCursor), after the sets.
        if (cursor is not null)
        {
            LogFormat.WriteUInt32(body, (uint)cursor.Segment);
            LogFormat.WriteUInt64(body, (ulong)cursor.Offset);
            LogFormat.WriteUInt64(body, (ulong)cursor.Slot);
            LogFormat.WriteUInt32(body, (uint)cursor.Pins.Length);

            foreach (CatalogPin pin in cursor.Pins)
            {
                LogFormat.WriteUInt64(body, (ulong)pin.Position);
                pin.Entry.Write(body.GetSpan(CommitEntry.Size));
                body.Advance(CommitEntry.Size);
                LogFormat.WriteUInt32(body, (uint)pin.EndSegment);
                LogFormat.WriteUInt64(body, (ulong)pin.EndOffset);
            }

            LogFormat.WriteUInt32(body, (uint)cursor.Settings.Length);

            foreach (SettingsPoint point in cursor.Settings)
            {
                LogFormat.WriteUInt64(body, (ulong)point.Position);
                body.GetSpan(1)[0] = (byte)point.Settings.DefaultAccessScope;
                body.Advance(1);
            }
        }

        byte[] directory = body.WrittenSpan.ToArray();
        byte[] header = DerivedFormat.EncodeHeader(DerivedFormat.KindCatalog, dataset, 0, position, positionHash, 0, directory.Length, SHA256.HashData(directory));

        await using IBlobWriter writer = await store.CreateAsync(Name, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(directory, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The catalog, when there is one that verifies for this dataset and whose windows chain from 0 to its position.</summary>
    internal static async ValueTask<(DerivedHeader Header, long Sequence, CatalogEntry[] Sets, CatalogCursor? Cursor)?> TryReadAsync(
        IDerivedStore store, DatasetId dataset, CancellationToken cancellationToken)
    {
        IReadableBlob blob;

        try
        {
            blob = await store.OpenAsync(Name, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        using (blob)
        {
            return TryParse(blob, dataset);
        }
    }

    /// <summary>A catalog's file, parsed: for this dataset, or, with none given, the one its header names.</summary>
    internal static (DerivedHeader Header, long Sequence, CatalogEntry[] Sets, CatalogCursor? Cursor)? TryParse(IReadableBlob blob, DatasetId? named)
    {
        DatasetId dataset;

        if (named is { } given)
        {
            dataset = given;
        }
        else
        {
            Span<byte> tail = stackalloc byte[DerivedFormat.HeaderLength];

            if (blob.Length.Value < DerivedFormat.HeaderLength || blob.Read(new ByteOffset(blob.Length.Value - DerivedFormat.HeaderLength), tail) != tail.Length)
            {
                return null;
            }

            dataset = DatasetId.Read(tail.Slice(8, DatasetId.Length));
        }

        {
            if (!DerivedFormat.TryReadHeader(blob, dataset, DerivedFormat.KindCatalog, out DerivedHeader header) || header.DirectoryOffset != 0 || header.Version < 4)
            {
                return null;
            }

            byte[] directory = new byte[header.DirectoryLength];

            if (blob.Read(new ByteOffset(0), directory) != directory.Length || !LogFormat.HashMatches(directory, header.DirectoryHash))
            {
                return null;
            }

            try
            {
                LogFormat.Reader reader = new(directory, header.To);
                long sequence = (long)reader.UInt64();
                uint count = reader.UInt32();

                if (count > directory.Length / 18)
                {
                    return null;
                }

                CatalogEntry[] sets = new CatalogEntry[count];
                long expected = 0;

                for (int i = 0; i < sets.Length; i++)
                {
                    ushort length = reader.UInt16();
                    string name = Encoding.UTF8.GetString(reader.Fixed(length));
                    long from = (long)reader.UInt64();
                    long to = (long)reader.UInt64();

                    if (from != expected || to <= from || ExtentSet.SequenceOf(name) < 0 || ExtentSet.SequenceOf(name) >= sequence)
                    {
                        return null;
                    }

                    sets[i] = new CatalogEntry(name, from, to);
                    expected = to;
                }

                CatalogCursor? cursor = reader.Remaining > 0 ? ReadCursor(ref reader, header) : null;
                reader.End();
                return expected == header.To ? (header, sequence, sets, cursor) : null;
            }
            catch (LogVerificationException)
            {
                return null;
            }
        }
    }

    // The cursor, or none when it does not agree with the catalog's position:
    // its last pin must be the position, with the position's hash.
    private static CatalogCursor? ReadCursor(ref LogFormat.Reader reader, DerivedHeader header)
    {
        int segment = (int)reader.UInt32();
        long offset = (long)reader.UInt64();
        long slot = (long)reader.UInt64();
        uint pins = reader.UInt32();

        if (pins > (uint)reader.Remaining / (8 + CommitEntry.Size + 12))
        {
            return null;
        }

        CatalogPin[] pinned = new CatalogPin[pins];

        for (int i = 0; i < pinned.Length; i++)
        {
            long position = (long)reader.UInt64();
            CommitEntry entry = CommitEntry.Read(reader.Fixed(CommitEntry.Size));
            pinned[i] = new CatalogPin(position, entry, (int)reader.UInt32(), (long)reader.UInt64());
        }

        uint count = reader.UInt32();

        if (count > (uint)reader.Remaining / 9)
        {
            return null;
        }

        SettingsPoint[] settings = new SettingsPoint[count];

        for (int i = 0; i < settings.Length; i++)
        {
            long position = (long)reader.UInt64();
            byte scope = reader.Byte();

            if (position > header.To || !Enum.IsDefined((AccessScope)scope))
            {
                return null;
            }

            settings[i] = new SettingsPoint(position, new DatasetSettings((AccessScope)scope));
        }

        return pinned.Length > 0 && pinned[^1].Position == header.To && pinned[^1].Entry.HashEquals(header.ToHash) && segment >= 0 && offset > 0
            ? new CatalogCursor(segment, offset, slot, pinned, settings)
            : null;
    }
}

/// <summary>
/// Turns a window's commits into an extent set's rows (ADR 0138): every
/// assertion and retraction as an event keyed in SPOG, sorted outside memory;
/// each quad's events paired into intervals and classified; the SPOG rows
/// kept in a spill, already in order; and every other order's rows re-keyed
/// from them and sorted. What it holds is the sorts' buffers.
/// </summary>
internal sealed class ExtentRowBuilder
{
    private const int SortBuffer = 1 << 16;

    private readonly SpillSpace _space;
    private readonly int _buffer;
    private readonly ExternalSort<ExtentEvent> _events;
    private readonly ExternalSort<ExtentRow>?[] _orders = new ExternalSort<ExtentRow>?[Orders.Count];
    private BlobName _spog;

    /// <summary>A builder whose sorts hold at most <paramref name="expected"/> records, and never more than 65,536, before they spill.</summary>
    internal ExtentRowBuilder(IDerivedStore store, string load, long expected)
    {
        _space = new SpillSpace(store, load);
        _buffer = (int)Math.Clamp(expected, 256, SortBuffer);
        _events = new ExternalSort<ExtentEvent>(_space, "events", _buffer);
    }

    /// <summary>One commit's effective delta, at its position.</summary>
    internal async ValueTask AddAsync(ReadOnlyMemory<Quad> asserted, ReadOnlyMemory<Quad> retracted, long position, CancellationToken cancellationToken)
    {
        for (int i = 0; i < asserted.Length; i++)
        {
            if (_events.Add(new ExtentEvent(Orders.Key(IndexOrder.Spog, in asserted.Span[i]), position, asserted: true)))
            {
                await _events.SpillAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        for (int i = 0; i < retracted.Length; i++)
        {
            if (_events.Add(new ExtentEvent(Orders.Key(IndexOrder.Spog, in retracted.Span[i]), position, asserted: false)))
            {
                await _events.SpillAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Pairs the events into classified rows and sorts them for every order.</summary>
    internal async ValueTask FinishAsync(CancellationToken cancellationToken)
    {
        _spog = _space.Next("rows");
        ExtentRow[] group = new ExtentRow[16];

        await using (RecordWriter<ExtentRow> spog = await RecordWriter<ExtentRow>.CreateAsync(_space.Store, _spog, cancellationToken).ConfigureAwait(false))
        {
            using SortedReader<ExtentEvent> events = await _events.FinishAsync(cancellationToken).ConfigureAwait(false);
            bool has = events.TryNext(out ExtentEvent next);

            while (has)
            {
                QuadKey key = next.Key;
                int count = 0;
                long from = 0;
                bool first = true;

                do
                {
                    // Effective deltas alternate (I2): a quad's first event
                    // in the window is a retraction only if it was asserted
                    // at the window's start.
                    if (next.Asserted == 1)
                    {
                        from = next.Position;
                    }
                    else
                    {
                        if (!first && from == 0)
                        {
                            throw new InvalidOperationException("A quad is retracted twice without an assertion between, at position " + next.Position + ".");
                        }

                        Add(ref group, ref count, new ExtentRow(key, from, next.Position, ExtentSection.Within));
                        from = 0;
                    }

                    first = false;
                    has = events.TryNext(out next);
                }
                while (has && next.Key.Equals(key));

                if (from != 0)
                {
                    Add(ref group, ref count, new ExtentRow(key, from, 0, ExtentSection.Within));
                }

                ClassifiedRows.Classify(group.AsSpan(0, count));

                for (int i = 0; i < count; i++)
                {
                    if (spog.Add(group[i]))
                    {
                        await spog.WriteBufferAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            await spog.PublishAsync(cancellationToken).ConfigureAwait(false);
        }

        await _events.DropAsync(cancellationToken).ConfigureAwait(false);

        for (int o = 1; o < Orders.Count; o++)
        {
            IndexOrder order = (IndexOrder)o;
            ExternalSort<ExtentRow> sort = new(_space, "order" + o.ToString(CultureInfo.InvariantCulture), _buffer);
            using RecordReader<ExtentRow> rows = await RecordReader<ExtentRow>.OpenAsync(_space.Store, _spog, cancellationToken).ConfigureAwait(false);

            while (rows.TryNext(out ExtentRow row))
            {
                Quad quad = Orders.Quad(IndexOrder.Spog, row.Key);

                if (sort.Add(new ExtentRow(Orders.Key(order, in quad), row.From, row.To, row.Section)))
                {
                    await sort.SpillAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            (await sort.FinishAsync(cancellationToken).ConfigureAwait(false)).Dispose();
            _orders[o] = sort;
        }
    }

    /// <summary>One order's rows of one section, in order, read again from the start.</summary>
    internal async ValueTask<IRowSource> RowsAsync(IndexOrder order, long section, CancellationToken cancellationToken)
    {
        RecordRowSource rows = order == IndexOrder.Spog
            ? new RecordRowSource(await RecordReader<ExtentRow>.OpenAsync(_space.Store, _spog, cancellationToken).ConfigureAwait(false))
            : new RecordRowSource(await _orders[(int)order]!.ReopenAsync(cancellationToken).ConfigureAwait(false));
        return new SectionRows(rows, section);
    }

    /// <summary>Deletes every spill.</summary>
    internal ValueTask DropAsync(CancellationToken cancellationToken) => _space.DeleteAllAsync(cancellationToken);

    private static void Add(ref ExtentRow[] group, ref int count, in ExtentRow row)
    {
        if (count == group.Length)
        {
            Array.Resize(ref group, group.Length * 2);
        }

        group[count++] = row;
    }
}
