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
- **SPARQL-free and SHACL-free.** Reads are through `IQuadSource` from
  `Varve.Rdf`; writes are an ordered list of assertions and retractions over
  terms.

```csharp
await using Dataset dataset = await Dataset.OpenAsync(
    new MemoryStorage(), new DatasetOptions { Clock = TimeProvider.System });

CommitResult result = await dataset.CommitAsync(new CommitRequest()
    .Assert(RdfTerm.Iri("http://example.org/s"u8), RdfTerm.Iri("http://example.org/p"u8),
            RdfTerm.Literal("chat"u8, "en"u8)));

using DatasetView view = dataset.Pin();
```

Layer 4 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
