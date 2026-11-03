// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Writes a dataset in an object store as v0.1.0-preview.8 writes it, from
// that release's published Varve.Store.ObjectStorage package: the log's
// chunks with chunk header 1, the writer record (format 1, before address
// and placement), and derived format 4. The store is in memory, behind the
// release's own IObjectStore; at the end every object is written as a file
// under objects/, named as the object is. The workload and expectations are
// those of the preview.1 fixture (docs/formats/, ADR 0216).
using System.Globalization;
using System.Text;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;

string root = Path.GetFullPath(args[0]);
int commits = int.Parse(args[1], CultureInfo.InvariantCulture);
int every = int.Parse(args[2], CultureInfo.InvariantCulture);
Clock clock = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
DatasetId id = new(new Guid("6a0e7b3c-1d2f-4a5b-8c9d-0e1f2a3b4c5d"));
static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + local));
static RdfTerm Literal(string lexical) => RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical));
static string Quad(string s, string p, string o, string? g) =>
    "<http://example.org/" + s + "> <http://example.org/" + p + "> " + o + (g is null ? "" : " <http://example.org/" + g + ">") + " .";
StringBuilder operations = new();

MemoryStore store = new();
string cache = Path.Combine(root, "cache");

await using (ObjectStorage storage = await ObjectStorage.OpenAsync(new ObjectStorageOptions
{
    Store = store,
    Cache = new CacheDirectory(cache),
    Instance = new WriterInstance("fixture-writer"),
#if ADDRESS
    Address = new WriterAddress("https://writer.fixture.example/"),
#endif
    Clock = clock,
    ProbeRandomness = new FixedRandomness(),
}))
{
#if ADDRESS
    // A writer that releases for a placement elsewhere (ADR 0163).
    storage.ReleaseOnClose = ReleaseReason.Placement;
#endif
    DatasetOptions options = new() { Clock = clock, Maintenance = MaintenanceMode.Off, MemtableLimit = new QuadCount(64) };
    await using Dataset dataset = await Dataset.CreateAsync(storage, id, options);

    for (int i = 1; i <= commits; i++)
    {
        clock.Now = clock.Now.AddSeconds(7);
        string n = i.ToString(CultureInfo.InvariantCulture);
        string a = (i % 17).ToString(CultureInfo.InvariantCulture), b = (i % 3).ToString(CultureInfo.InvariantCulture);
        string c = (i % 11).ToString(CultureInfo.InvariantCulture), d = (i % 5).ToString(CultureInfo.InvariantCulture);
        CommitRequest request = new CommitRequest()
            .Assert(Iri("s" + a), Iri("p" + b), Literal("v" + n), Iri("g" + b))
            .Assert(Iri("s" + c), Iri("q"), Iri("o" + d));
        operations.Append(n).Append("\t+\t").Append(Quad("s" + a, "p" + b, "\"v" + n + "\"", "g" + b)).Append('\n');
        operations.Append(n).Append("\t+\t").Append(Quad("s" + c, "q", "<http://example.org/o" + d + ">", null)).Append('\n');

        if (i > 4)
        {
            int j = i - 4;
            string m = j.ToString(CultureInfo.InvariantCulture), e = (j % 17).ToString(CultureInfo.InvariantCulture), f = (j % 3).ToString(CultureInfo.InvariantCulture);
            request.Retract(Iri("s" + e), Iri("p" + f), Literal("v" + m), Iri("g" + f));
            operations.Append(n).Append("\t-\t").Append(Quad("s" + e, "p" + f, "\"v" + m + "\"", "g" + f)).Append('\n');
        }

        await dataset.CommitAsync(request);
        await dataset.MaintainAsync();

        if (i % every == 0)
        {
            await dataset.CheckpointAsync(new Position(i));
        }
    }

    Console.WriteLine("head " + dataset.Head.Value + ", checkpoints " + string.Join(",", dataset.Checkpoints.Select(p => p.Value)));
}

#if READER
// A follower, announced (ADR 0160): its reader record is written while it
// is open, so the store is written out before it closes.
string readerCache = Path.Combine(root, "reader-cache");

await using (ObjectStorage follower = await ObjectStorage.OpenAsync(new ObjectStorageOptions
{
    Store = store,
    Cache = new CacheDirectory(readerCache),
    Access = ObjectStorageAccess.ReadOnly,
    Reader = new ReaderId("fixture-reader"),
    Clock = clock,
}))
{
    await using Dataset view = await Dataset.OpenAsync(follower, new DatasetOptions { Clock = clock, Maintenance = MaintenanceMode.Off });
    Dump(store, root);
}

Directory.Delete(readerCache, recursive: true);
#else
Dump(store, root);
#endif

Directory.Delete(cache, recursive: true);

static void Dump(MemoryStore store, string root)
{
    foreach ((string name, byte[] bytes) in store.Objects)
    {
        string path = Path.Combine(root, "objects", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
Directory.CreateDirectory(Path.Combine(root, "expectations"));
File.WriteAllText(Path.Combine(root, "expectations", "operations.tsv"), operations.ToString());

// The probe key's random part, fixed so the same release writes the same bytes.
sealed class FixedRandomness : IProbeRandomness
{
    public void Fill(Span<byte> destination) => destination.Fill(0x5a);
}

// An object store in memory with conditional writes, as the release's own
// tests keep one; versions are a counter.
sealed class MemoryStore : IObjectStore
{
    private readonly SortedDictionary<string, (byte[] Bytes, long Version)> _objects = new(StringComparer.Ordinal);
    private long _versions;

    public IEnumerable<(string Name, byte[] Bytes)> Objects => _objects.Select(o => (o.Key, o.Value.Bytes));

    public ValueTask<ObjectVersion?> CreateIfAbsentAsync(ObjectName name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (_objects.ContainsKey(name.Value))
        {
            return new((ObjectVersion?)null);
        }

        _objects[name.Value] = (content.ToArray(), ++_versions);
        return new(Version(_versions));
    }

    public ValueTask<ObjectVersion?> ReplaceIfUnchangedAsync(ObjectName name, ObjectVersion expected, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (!_objects.TryGetValue(name.Value, out var found) || Version(found.Version) != expected)
        {
            return new((ObjectVersion?)null);
        }

        _objects[name.Value] = (content.ToArray(), ++_versions);
        return new(Version(_versions));
    }

    public ValueTask<ObjectRead?> ReadAsync(ObjectName name, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
    {
        if (!_objects.TryGetValue(name.Value, out var found))
        {
            return new((ObjectRead?)null);
        }

        long start = Math.Min(offset.Value, found.Bytes.Length);
        int count = (int)Math.Min(found.Bytes.Length - start, length.Value);
        return new(new ObjectRead(found.Bytes.AsSpan((int)start, count).ToArray(), Version(found.Version), new ByteCount(found.Bytes.Length)));
    }

    public ValueTask<IReadOnlyList<ObjectEntry>> ListAsync(ObjectName prefix, CancellationToken cancellationToken) =>
        new((IReadOnlyList<ObjectEntry>)[.. _objects.Where(o => o.Key.StartsWith(prefix.Value, StringComparison.Ordinal))
            .Select(o => new ObjectEntry(new ObjectName(o.Key), new ByteCount(o.Value.Bytes.Length), Version(o.Value.Version)))]);

    public ValueTask<bool> DeleteAsync(ObjectName name, CancellationToken cancellationToken) => new(_objects.Remove(name.Value));

    private static ObjectVersion Version(long version) => new("v" + version.ToString(CultureInfo.InvariantCulture));
}

sealed class Clock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}
