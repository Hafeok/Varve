# Varve.Store.ObjectStorage

A Varve dataset in an object store with conditional writes (ADRs 0134–0136,
0139). Provider-neutral: it is written against `IObjectStore`, five
operations, and a provider is an adapter of its own — `Varve.Store.Azure`,
`Varve.Store.S3`.

- **A store that enforces conditional writes, or none.** A writer's open
  first probes under `probe/{random}` that a second create-if-absent and a
  replace against a stale version are both refused, and fails with
  `ConditionalWritesNotEnforcedException`, naming the store, when either is
  not (ADR 0134). Garage is one such store. The supported stores are those
  the contract suite's race property has passed: Azure Blob Storage and
  RustFS.
- **The log as write-once chunk objects.** Each flush is one create-if-absent
  of the next log slot, `log/NNNNNNNN.chunks/CCCCCCCC`; a sealed segment is
  consolidated into `log/NNNNNNNN.seg`, byte for byte the file backend's
  segment. Declares `Durability.Replicated`: a commit returns once the store
  acknowledged its chunk.
- **One writer by an epoch, no lease, no timer.** Opening for write claims
  the next epoch in the `writer` record and creates the next log slot as a
  fence, so a writer still running elsewhere appends nothing more: its next
  create finds the slot taken, it fails that batch with `writer-superseded`
  and stays open read-only. Closing releases the epoch. `TakeOverAsync` is an
  operator's takeover from a named epoch.
- **Checkpoints as extents in the store** (ADR 0139). A checkpoint's extent
  sets and the catalog that names them are in the store under `derived/`,
  where a follower and the next writer find them; the catalog is fenced by the
  epoch, so a superseded writer cannot swap it. Everything else derived lives
  in the local cache.
- **A bounded page cache.** Extents are read through 1 MiB pages fetched on
  first use; `CacheBytes` bounds the cache and eviction never changes a
  result.
- **Followers.** `ObjectStorageAccess.ReadOnly` claims nothing and writes
  nothing, so it needs only read access: it attaches the catalog and replays
  the log after its cursor.

```csharp
using AzureBlobStore store = AzureBlobStore.Create(new AzureBlobStoreOptions
{
    ContainerUrl = new Uri("https://account.blob.core.windows.net/dataset"),
    Clock = TimeProvider.System,
});

await using ObjectStorage storage = await ObjectStorage.OpenAsync(new ObjectStorageOptions
{
    Store = store,
    Cache = new CacheDirectory("/var/cache/varve/dataset"),
    Instance = new WriterInstance(Environment.MachineName),
    Clock = TimeProvider.System,
    ProbeRandomness = new SystemRandomness(),
});

await using Dataset dataset = await Dataset.OpenAsync(storage, new DatasetOptions { Clock = TimeProvider.System });

// The probe key's random part: the store draws no randomness of its own.
sealed class SystemRandomness : IProbeRandomness
{
    public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
}
```
