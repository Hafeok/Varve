# Managed storage engines — research note

**This is a research note, not an ADR and not a decision.** It informs milestone
6, where the durable backend is chosen. Nothing here binds anything.

Package facts were verified on **2026-09-21** by downloading each `.nupkg` from
nuget.org and inspecting the nuspec and the archive contents — target
frameworks, declared licence, declared dependencies, and whether the package
carries anything under `runtimes/` or `native/`. Versions are the latest stable
at that date. Nothing below is taken from memory.

## What we are actually shopping for

The most useful thing this note can do is stop a category error, because the
design needs **two different things** and only one of them is a key-value store.

**`log/` does not want a storage engine at all.** It wants an append-only
segment writer with sealing and range reads
([ADR 0018](../adr/0018-storage-abstraction.md)). It has no keys, no updates, no
lookups — it is written once and read in order.
[ADR 0015](../adr/0015-checkpoints-and-reads.md) says there is no compaction in
the destructive sense, and §2 of the specification says the log is never
rewritten. **An LSM engine's defining behaviour — merge runs and discard
superseded records — is exactly right for `derived/` and catastrophic for
`log/`.** Any evaluation that treats "we need a storage engine" as one question
will end up pointing an engine at the log.

**`derived/` wants ordered runs.** Checkpoints are immutable sorted runs that
are *directly queryable* (T2), and the quad indexes are ordered scans over
dictionary-encoded ids. So the requirement is an ordered key-value store with
cheap immutable snapshots and efficient range scans over fixed-width keys — and
the engines below are candidates for that half only.

### The bar

| | Requirement | Source |
|---|---|---|
| 1 | 100% managed. A package that ships a native asset is out, with no exception process. | constraint 1, ADR 0009 |
| 2 | Native AOT and trimming safe. No `Reflection.Emit`, no reflection over names in hot paths. | constraint 2 |
| 3 | Works in the browser, or is confined to hosts where it does. | constraint 3 |
| 4 | Dependency cost is real and is paid by every consumer. | constraint 4 |
| 5 | **Fits immutable sorted runs**, and does not assume it owns the log. | ADR 0015 |

**Constraint 3 is the sharpest filter and none of the candidates clear it
explicitly.** Not one advertises a browser story. Every one of them assumes a
file system, which Blazor WebAssembly does not provide
([ADR 0018](../adr/0018-storage-abstraction.md)). Whatever is chosen, the
browser backend is either written by us or the feature is not available there.

**Ruled out before evaluation:** RocksDB (`RocksDbSharp`), LMDB, SQLite
(`Microsoft.Data.Sqlite`), and everything else that ships a native binary. Not
because they are bad — they are the obvious answers and they are excellent —
but because constraint 1 admits no exception process. This is the single
largest cost the brief's constraints impose, and it is worth naming as such
rather than pretending the managed field is equally strong.

## Candidates

### ZoneTree 1.9.8 — MIT

| | |
|---|---|
| **Native assets** | **None** |
| **Target frameworks** | net6.0, net7.0, net8.0, net9.0, **net10.0** |
| **Dependencies** | `K4os.Compression.LZ4`, `ZstdSharp.Port` — both verified to carry **no native assets** |
| **Shape** | LSM-tree, ordered, transactional, ACID |

The closest fit in the managed field, and the only candidate that is both
current (a net10.0 target) and structurally right (an LSM's sorted runs are what
checkpoints and quad indexes want).

- **Fits requirement 5 for `derived/`** and would have to be kept away from
  `log/`, for the reason above: its compaction discards superseded records.
- Actively working on AOT and trimming — `IsAotCompatible` for net7.0 through
  net10.0, with reflection-based `System.Text.Json` being replaced by source
  generation ([PR #176](https://github.com/ZoneTree/ZoneTree/pull/176)). Treat
  this as in progress rather than done: requirement 2 needs verifying against a
  real `PublishAot` build, not against an intention.
- Two transitive compression dependencies. Managed, and both are the kind of
  focused package ADR 0009's policy admits — but they are three packages in a
  consumer's graph where the brief would prefer zero.
- **It owns its own file I/O.** There is no seam where our storage abstraction
  could be substituted, so the browser is out and the memory backend would be
  its own thing. That is the deciding question to put to it at milestone 6.

Sources: [ZoneTree on GitHub](https://github.com/ZoneTree/ZoneTree),
[NuGet](https://www.nuget.org/packages/ZoneTree/).

### Microsoft.FASTER.Core 2.6.5 — MIT

| | |
|---|---|
| **Native assets** | **None** |
| **Target frameworks** | netstandard2.0, netstandard2.1, net6.0, **net7.0 — and no further** |
| **Dependencies** | `Microsoft.Extensions.Logging`, `System.Interactive.Async`, `Microsoft.Bcl.AsyncInterfaces`, three compatibility facades |

Microsoft Research's hybrid log and key-value store. Genuinely fast and
genuinely managed.

- **Stale as a package.** The newest target framework is net7.0. Development
  moved into Garnet's Tsavorite fork (below), which is not published separately.
- **Fights requirement 5.** FASTER is built around a hash index for point
  operations over a hybrid log. Ordered range scans over dictionary-encoded quad
  keys — which is what every RDF index is — are not what it is for. Choosing it
  would mean building the ordering ourselves on top, which is most of the work.
- `System.Interactive.Async` and the facades are a heavier dependency tail than
  ZoneTree's.

Source: [microsoft/FASTER](https://github.com/microsoft/FASTER/blob/main/README.md).

### Tsavorite (inside microsoft/garnet) — MIT

The active evolution of FASTER: tiered storage, non-blocking checkpointing,
recovery, operation logging, multi-key transactions.

- **Not published as a standalone package.** `Tsavorite`, `Tsavorite.Core` and
  `Microsoft.Tsavorite` all return 404 on nuget.org.
- The package that *does* ship it, **`Microsoft.Garnet` 2.1.8, carries native
  assets** — `runtimes/linux-arm64/native/libnative_device.so` and siblings —
  and depends on `KeraLua` and `diskann-garnet`. **Out under constraint 1** as a
  package, without argument.
- Vendoring the source is permitted by MIT and is a large adoption: a fork of a
  fast-moving storage engine becomes ours to maintain, and the brief's "minimal
  dependencies" does not mean "copy them in instead".
- Worth reading for its checkpointing and recovery design regardless — the same
  way Oxigraph is read for behaviour.

Sources: [Tsavorite in
garnet](https://github.com/microsoft/garnet/tree/main/libs/storage/Tsavorite/),
[Microsoft.Garnet on NuGet](https://www.nuget.org/packages/Microsoft.Garnet/).

### LiteDB 5.0.21 — MIT

| | |
|---|---|
| **Native assets** | None |
| **Target frameworks** | net45, netstandard1.3, **netstandard2.0 — nothing newer** |
| **Dependencies** | `NETStandard.Library`, `System.Buffers`, `System.Reflection.TypeExtensions`, `System.Security.Cryptography.Algorithms` |

An embedded document store with a B-tree and its own single-file page format.

- **Fights requirement 5 and constraint 5 both.** It is a document database, not
  an ordered key-value store over fixed-width keys, and a netstandard2.0-era
  codebase does not use `Span`, pipelines or pooled buffers — which is the
  performance model the brief requires, not a preference.
- `System.Reflection.TypeExtensions` in the dependency list is a signal worth
  following up against requirement 2.

### DBreeze 1.138 — licence in-package, needs checking

| | |
|---|---|
| **Native assets** | None |
| **Target frameworks** | net35 through net8.0 and netstandard1.6/2.0/2.1 — an unusually wide spread |
| **Dependencies** | None at all |

A mature managed embedded database with a B+tree. Zero dependencies is a real
virtue under constraint 4.

- The very wide framework spread implies a codebase written to a much older
  common denominator, with the same `Span`-era objection as LiteDB.
- The package declares a licence *file* rather than an SPDX expression, so the
  terms need reading before adoption rather than assuming.

### Lucene.NET — Apache-2.0

Listed to be set aside deliberately. It is not a key-value store and is not a
candidate for either half of this design.

- Stable is **3.0.3**, which is very old; the 4.8 line has been in beta for
  years and is at `4.8.0-beta00018`.
- It is, however, the reference implementation of the pattern this design is
  built on: **immutable segments, merged in the background, never updated in
  place.** Read it for that, and consider it later as the *full-text projection*
  ([ADR 0016](../adr/0016-projection-contract-and-subscriptions.md)) rather than
  as the store — where the `KeyId`-tagging obligation for erasure will apply to
  it.

## Write our own

The honest comparison, because the thing we would write is much smaller than a
general engine.

**What it would have to do:** append-only segments with sealing and range reads
for `log/`; immutable sorted runs with range scans and a background merge for
`derived/`; a block layout over fixed-width dictionary-encoded keys; and a
snapshot that is just "these runs", since the runs are immutable.

**What it would *not* have to do**, which is most of what a general engine
carries: arbitrary-length keys and values, secondary indexes, a query layer,
transactions across unrelated keys, pluggable comparators, or a general-purpose
cache. Those are the parts that make an engine large.

**In its favour.** It is the only option that can sit on the storage abstraction
rather than under it, which is what makes the browser possible at all. It has no
dependencies. It is AOT-safe because we write it that way. It cannot compact the
log, because we would not give it the ability. And the design's central
property — immutable sorted runs — is the simplest thing in the storage
literature to get right.

**Against.** Durability, crash recovery and merge scheduling are where storage
engines are hard, and they are hard in ways that show up months later as
corruption rather than as a failing test. §10's property tests
(crash-at-any-record-boundary recovery, checkpoint equivalence, determinism)
are the mitigation, and they are a serious body of work in their own right.

## What milestone 6 should actually do

1. **Split the question first.** Decide `log/` and `derived/` separately. `log/`
   is almost certainly ours regardless, because no engine offers an append-only
   segment store that refuses to compact.
2. **Test requirement 2 against a build**, not a claim. `PublishAot` with
   IL-prefixed warnings as errors, per ADR 0004.
3. **Test requirement 3 first, not last.** Ask each candidate whether it can run
   over a substitutable storage layer at all. This is the question ZoneTree most
   needs answered, and the answer is likely to decide the whole thing.
4. **Benchmark with BenchmarkDotNet on a stated dataset**, as the brief
   requires, against a from-scratch sorted-run implementation as the control.
   A candidate that is not clearly better than the control is not worth the
   dependency.

## Honest limits of this note

No candidate was built, run or benchmarked. Package metadata says what a package
declares, not how it behaves: "no native assets in the package" is verified,
"AOT-safe" and "works in a browser" are not. The AOT claims for ZoneTree are
from its own repository and are in progress. None of this replaces milestone 6's
evaluation; it narrows what that evaluation has to look at.
