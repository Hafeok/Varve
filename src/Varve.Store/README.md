# Varve.Store

An event-sourced RDF store. The log is the source of truth and every index is
a projection of it.

- **The log records what changed, not what was asked for.** Asserting a present
  quad, retracting an absent one, and asserting then retracting an absent one
  contribute nothing; a request that changes nothing is `NoChange` and leaves
  no trace.
- **One sequencer, optional expected position.** `Conflict(head)` is a normal
  answer, not an error.
- **Pinned reads and as-of reads are different things.** `Pin()` is a snapshot
  for one operation. `AsOfAsync(position)` is time travel, served from the
  nearest checkpoint plus an overlay of the log tail.
- **A header chain** makes two copies of one dataset that were continued
  independently detectably divergent.
- **Pre-commit validators** bound to the dataset gate every commit; a request
  may add its own. A validator sees the state the commit would produce and
  the delta, and nothing else.
- **A staging view** over a pinned read names terms the dataset does not hold
  yet, so that several changes can be composed over an overlay and
  submitted as one commit — what SPARQL Update's integration does.
- **On disk or in memory.** `FileStorage` keeps a dataset directory of plain
  files — `log/`, the source of truth, never rewritten, and `derived/`, which
  can be deleted and is rebuilt — and flushes every commit to the device.
  `MemoryStorage` keeps the same bytes in memory. The on-disk format is
  versioned and read for ever from the first release that writes it.
- **Opens in the time of its tail, not its size.** The term dictionary lives
  in the projection's runs on disk, read through lookups by id and by term;
  opening reads the log after the newest persisted state and loads nothing per
  term. Checkpoints, written on demand or by a `CheckpointPolicy`, bound what
  an as-of read and an open replay.
- **Bulk loads.** `BeginBulkLoadAsync` takes any parser's quads as they are
  parsed; `BulkLoadOptions.Workers` threads resolve and spill them while the
  parser reads (ADR 0107), sorted outside memory within
  `BulkLoadOptions.MemoryBytes`, merged with the dataset in one sequential
  pass, and committed as one commit — with the dataset's validators reading
  the delta on disk. The result is the same at any worker count.
- **Lookups by term probe a filter first.** Each run on disk carries a
  blocked Bloom filter over its terms (ADR 0108, derived format 3), so a
  commit's new term costs one probe per run and an index read only where the
  filter says maybe; a format-2 dataset is read as before and migrates as
  maintenance rewrites its runs.
- **Replicas by copying files.** `ShipAsync` copies the log up to a position
  and a checkpoint into another storage, which opens at exactly that position.
- **Recovers from crashes.** A torn or unclosed tail is ignored, a copy taken
  while the dataset was being written opens at its last closed commit, and
  damage that no crash produces refuses rather than guesses.
- **SPARQL-free and SHACL-free.** Reads are through `IQuadSource` from
  `Varve.Rdf`; writes are an ordered list of assertions and retractions over
  terms.

```csharp
await using FileStorage storage = await FileStorage.OpenAsync(
    new DatasetDirectory("my-dataset"), new FileStorageOptions { Clock = TimeProvider.System });
DatasetOptions options = new() { Clock = TimeProvider.System };

await using Dataset dataset = await Dataset.CreateAsync(storage, new DatasetId(Guid.NewGuid()), options);
// Later: await Dataset.OpenAsync(storage, options);

CommitResult result = await dataset.CommitAsync(new CommitRequest()
    .Assert(RdfTerm.Iri("http://example.org/s"u8), RdfTerm.Iri("http://example.org/p"u8),
            RdfTerm.Literal("chat"u8, "en"u8)));

using DatasetView view = dataset.Pin();

// A bulk load: the parser's handler is the load's Assert.
await using BulkLoad load = await dataset.BeginBulkLoadAsync();
NQuadsParser.Parse(File.OpenRead("data.nq"), load.Assert, new ParseOptions { Syntax = RdfSyntax.NQuads });
CommitResult loaded = await load.CommitAsync(new CommitMetadata());
```

In a browser, `Varve.Store.Browser` provides the storage: OPFS from a worker,
IndexedDB elsewhere.

Layer 4 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
