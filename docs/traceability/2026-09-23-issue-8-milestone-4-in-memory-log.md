# Milestone 4 — the in-memory log and the default quad projection

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#8](https://github.com/Hafeok/Varve/issues/8) |
| **Date** | 2026-09-23 |
| **Tool** | Claude Code 2.1.280, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5` (Claude Opus 5.5), configured and served — confirmed from the session's own metadata, not from memory |
| **Session identifier** | `session_01CShzBwo799v1diF4uwtEkA` |
| **Branch** | `claude/inspiring-hopper-z7ywrj`, landing on `main` as one pull request (ADR 0034's amendment) |
| **Commits** | 13, and one merge of `main` |

## The prompt

### First message

> Milestone 4: the in-memory log, the default quad projection, pinned and as-of reads, checkpoints as a fold, and the property tests from the specification. This is the first code against `docs/spec/log-and-projection-model.md` and the first test of the storage thesis. The specification is the authority; ADRs 0010 to 0022 and 0028 decide the rest. Read them all before planning.
> Another session is working on trunk in `tools/repo-standard/` at the same time. Do not touch that directory. Pull before every push. Your ADR numbers start at 0040; if a pull shows a collision, renumber yours and say so in the report.
> Plan first: present the plan, the ADR list, and the public API sketch (section B) before writing code. One commit per step, conventional commits, every commit references an issue, DCO sign-off, trunk-based; `AGENTS.md` applies. Every public member is a baseline entry, so the API sketch is where the review happens.
>
> A. What is built
> Four packages and their tests, one vertical slice at a time, each slice ending in passing tests:
>
> * `Varve.Rdf`: the quad source contract from ADR 0022 (term handle, internalise, externalise, source-supplied equality, the graph-matching modes from the 3a close-out), the overlay quad source from ADR 0017, and an in-memory dataset that implements the contract with its own interning table. Layer 1.
> * `Varve.Store`: the log, commits and records, the sequencer, the term dictionary with the three id classes and inline small values (ADR 0012), the default quad projection, the projection contract, the pre-commit validator hook, `Pin()`, as-of reads, `Diff`, checkpoints, subscriptions, and the dataset failed state. Layer 4. SPARQL-free and SHACL-free by ADR 0005; the analyzers enforce the layer.
> * `Varve.Store.Memory` (or the equivalent name the storage ADR 0018 gives it): the in-memory implementation of the storage abstraction: append-only segment store plus derived blob store, durability declared as none. Layer 4 or 5 per 0018; state which and why. The file and browser backends are milestone 6.
> * `Varve.Store.Tests`: the spec's section 10 property tests, with CsCheck, as the definition of done.
>
> Not built: erasure mode, the key store, private terms beyond the reserved id class (the class exists in the id layout; nothing allocates it), archive, the file backend, any SPARQL.
>
> B. Public API sketch, in the plan
> Present the surface of the three packages before code. Constraints:
>
> * Term handles are the 64-bit opaque struct from ADR 0022. Equality on handles is supplied by the source; the evaluator-facing contract exposes an `IEqualityComparer` over handles, and the store's comparer is id equality for canonical and blank ids (private terms are not allocated in this milestone, so value comparison is declared and tested with a stub comparer only).
> * The quad source contract has the three graph modes: default graph, one named graph, any named graph; plus, if 0022's amendment kept it, the explicit union. Wildcards in subject, predicate and object positions; a scan returns handles, allocation-free per quad on the hot path, with `[HotPath]` marked.
> * The commit API takes an ordered list of assert and retract operations over terms (not handles; the caller does not know ids yet), metadata (timestamp is assigned by the sequencer, never accepted from the caller), an optional expected position, and returns one of `Committed(P)`, `NoChange(P)`, `Conflict(P)`, `Rejected(report)`, `Unavailable`. Blank node labels in a request are request-scoped.
> * The pre-commit validator contract receives `Overlay(G_head, δ)` as a quad source and `δ` as a delta, returns accept (with optional attachment) or reject (with a report term set). It is a contract in `Varve.Store` over `Varve.Rdf` types only.
> * The projection contract is `apply(commit)` with `pos` persisted atomically with state, idempotent on `pos ≤ pos(π)`, rebuildable from 0 or from a checkpoint.
> * Subscriptions: `Subscribe(from, filter)` delivering closed commits in order, at-least-once, consumer-owned position, filtered deltas, `Erasure` and `Settings` commits always delivered.
> * Settings: the `Settings` commit kind and the fold (ADR 0021); erasure mode is read as always off in this milestone.
> * Records: the commit-versus-record split and the closing flag are in the in-memory log's model from the start (ADR 0013), even though nothing can crash in memory; the recovery property test simulates a truncated tail.
> * The header chain (ADR 0014): computed and verified on open, with the fixed value for position 1.
>
> Say in the sketch which members are `[HotPath]`, and which are deliberately not public yet.
>
> C. Definition of done: the property tests
> Every row of the specification's section 10 that does not concern erasure or the file backend, as a CsCheck property, plus the model-based test that ties them together:
>
> * A reference model: a naive fold over the same request sequence (sets of quads, positions, a dictionary as a map), against which the store's answers are checked. Generators produce request sequences with asserts, retracts, redundant operations, blank node labels reused across requests, expected positions both right and stale, and validators that accept, reject, and attach.
> * I2 effective delta; I3 dictionary closure and no trace from rejected or empty requests; I5 monotone time and as-of by timestamp; I6 header chain breaks on any byte change and detects divergent continuations; I7 checkpoint plus tail equals full replay; I8 rebuilt projection equals incremental projection at every position; R1 pinned source stable across later commits; R2 and R4 as-of via overlay equals full replay; R3 `Overlay(G_P1, Diff(P1,P2)) = G_P2` and delta composition associative; records: a simulated truncated tail recovers to the last closed commit; determinism: two stores fed the same sequence with an injected clock produce identical log bytes; the failed state: a projection that throws puts the dataset into the failed state, `Pin()` and commit fail explicitly, rebuild clears it.
> * Allocation: a commit of N quads allocates O(N) once for the delta and nothing per quad in the index update; a scan allocates nothing per quad. Assert both the way 3a and 3b did.
>
> D. Gates that come due
>
> * Public API baseline for three new packable projects; BannedSymbols per the existing list plus, per ADR 0004's note from milestone 2, a `Varve.Store` entry banning ambient clock and randomness (`DateTime.Now`, `DateTimeOffset.Now`, `Stopwatch.GetTimestamp` outside an injected clock, `Random`, `RandomNumberGenerator`), because the determinism property depends on it. Prove the ban with a fixture.
> * AOT and browser-wasm smoke extended to open an in-memory store, commit, pin, and read as-of.
> * ADRs: 0040 onward for anything the milestone decides that the existing ADRs do not: the in-memory backend's package placement, the checkpoint representation in memory, the subscription delivery mechanism, and the reference model as a test asset. Check each against every accepted ADR and the specification; if the specification needs a change, stop and report it as a proposed change rather than patching it.
> * Benchmarks, last and not gating: commit throughput and scan throughput on a stated generated dataset; index size per quad. These are the numbers ADR 0012's revisit condition asks for; report them against that condition explicitly.
>
> Non-goals
> No file or browser backend, no erasure mode, no SPARQL, no SHACL, no server. No publishing. Nothing in `tools/`.
>
> Report
> Filed as the traceability record and the merge body. In addition: the API as shipped against the sketch, every property with its iteration count and any shrunk counterexample found during development (these are worth more than the passing run), the benchmark numbers with hardware and the verdict on 0012's revisit condition, proposed spec changes, and what milestone 5 (Xsd, then SPARQL parser, algebra and evaluator over this projection) needs from the maintainer.

### The maintainer's decisions on the plan

> Decisions:
>
> 1. Write ADR 0046: §8 delivers Settings and Erasure commits regardless of filter; amend 0016; spec to version 1.2 with a dated change entry. Implement to it.
> 2. The memory backend lives inside Varve.Store. Prove the contract is implementable from outside with a trivial second backend in Varve.Store.Tests using public members only. Adjust 0040 accordingly; the file backend at milestone 6 is the real external proof.
> 3. Q1: in-process form now via RequestTerm.Existing(handle); skolem IRI scheme decided at milestone 7 with the server. Record that split in 0044.
> 4. Land as one PR at the end; I merge with the admin override. If the signing ADR does not already say that cloud sessions cannot push to main and land through a PR, amend it in this PR.
>
> Spec changes: accept §10 "for crash-free histories". §1: the named-graph scope is a declaration recorded in metadata, never enforced by the store; validators may enforce it; write that sentence. Accept the §6 note. Note the IQuadSource sync versus storage async risk in the roadmap under milestone 6.
>
> The rest of the plan, ADRs 0040–0045 and the API sketch, is approved as presented. Proceed.

## The report

### What was built

- **`Varve.Rdf` (layer 1).** Most of it already existed from the 3a close-out:
  `TermHandle`, `IQuadSource` with the four graph modes (the explicit union
  `Any` kept), and `InMemoryDataset` with its own interning table. Added:
  **`QuadDelta`** (the delta and the specification's `;`) and **`QuadOverlay`**
  (`(B \ R) ∪ A` merged at scan time, exact for any delta), and the
  `[HotPath]` marks on the existing scan and lookup.
- **`Varve.Store` (layer 4)**, which also holds **`MemoryStorage`** (maintainer's
  decision 2, ADR 0040): the storage contract; the log in a provisional,
  versioned encoding (ADR 0045) with records, a closing flag, a header chain
  from a domain-separated genesis, a content hash, and each header's own
  stored hash; the sequencer (T1, T5); the dictionary with canonical, blank and
  inline ids (canonical `xsd:integer` in 56 bits and `xsd:boolean`); the
  default projection as immutable sorted runs in six orders (ADR 0041);
  `Pin()`, as-of by position and by timestamp, `Diff`, checkpoints stored as
  blobs and scanned in place; settings as a fold; subscriptions pulled from the
  log (ADR 0042); `IProjection` with `CatchUpAsync` and `RebuildAsync`; the
  failed state; `LogChain.FindDivergenceAsync`.
- **`Varve.Store.Tests`**, with the reference model (ADR 0043).
- **Gates**: public API baselines for `Varve.Rdf` and `Varve.Store`; the
  ambient clock and randomness ban as `eng/BannedSymbols.Deterministic.txt`,
  switched on by `VarveDeterministic`, and proven by the
  `Varve.Fixture.AmbientClock` fixture; the store in the AOT and browser smoke
  apps; a `test-store` job in `eng/ci.cs`, `ci.yml` and `publish.yml`.
- **ADRs 0040–0046**, specification 1.2, a dated amendment to 0016, and a dated
  amendment to 0034: a cloud session bound to a branch lands through a pull
  request. **No numbering collision**: `main` gained 0038 during the session.

### The API as shipped, against the sketch

Shipped as sketched, except:

| Sketch | Shipped | Why |
|---|---|---|
| `Varve.Store.Memory`, a fourth package | `MemoryStorage` in `Varve.Store` | Maintainer's decision 2 |
| `AsOfAsync(DateTimeOffset)` overload | `AsOfTimestampAsync(DateTimeOffset)` | RS0026: two overloads with optional parameters |
| `RebuildAsync(CancellationToken)` for the default projection | `RebuildDefaultProjectionAsync` | RS0026, same reason |
| `SubscriptionFilter.Graph(…)`, `.Pattern(…)` | `ForGraph`, `ForPattern` | The names are taken by the filter's own properties |
| `Subscribe(from, filter)` | `Subscribe(from, filter, cancellationToken)` | `[EnumeratorCancellation]`; the consumer cancels a wait |
| `RequestTerm` | adds `None`, `IsNone`, `FromTerm` | `default(RequestTerm)` is the default graph; CA2225 asks for a named alternative to the implicit conversion |
| `MemoryStorage.FromSegments(log)` | adds optional derived blobs | a copy of `log/` and `derived/` together, which I7's test needs |

Also public and not in the sketch's text, but implied by it: equality members on
the structs, the standard exception constructors, `CommitResult.ToString`.
`[HotPath]` is on every scan's `MoveNext`, every `Contains`, `QuadDelta.Asserts`
and `Retracts`, `SubscriptionFilter.Matches`, the store's comparer, key
comparison, run search and merge, and the index update's key building.

**Deliberately not public:** the id layout and inline set, the record and
header encoding and hashes, the default projection's type, a checkpoint policy,
erasure mode (the fold carries it, always off), the key store, the classifier,
the selector, creating an `Erasure` commit, the private-term comparison hook,
archive and `Detach`, continuing from a foreign head, a bulk-load API.

**Two internal seams**, through `InternalsVisibleTo(Varve.Store.Tests)` as
VARVE0003 (reserved) permits: `DatasetOptions.DefaultProjectionFault` for the
failed-state test, and `Dataset.AppendErasureAsync` for delivering an `Erasure`
commit before anything else can produce one.

### Every property, with its iteration count

| Property | §10 row | Iterations |
|---|---|---:|
| Composition is the specification's formula | R3 | 2,000 |
| Composition is associative **over a chain of exact deltas** | R3 | 2,000 |
| The empty delta is the identity | R3 | 2,000 |
| A composed delta never asserts and retracts one quad | R3 | 2,000 |
| Applying a composition is applying each in turn | R3 | 2,000 |
| The inverse undoes | R3 | 2,000 |
| An overlay is `(B \ R) ∪ A`, every pattern and graph mode, exact deltas or not | R4 | 2,000 |
| **The store agrees with the reference model** after every request, and over the run on I2, I3, I5, R1, R2, R3, R4, I7, I8 and the settings fold | all | 1,000 scripts of up to 24 steps |
| The same, with 64-byte records and 1 KiB segments | records | 333 |
| A log cut at every byte offset recovers to the last closed commit with the model's state there — which is I8 at every position — and sampled cuts continue and reopen | records, I8 | 40 × 2 configurations × every offset |
| The same requests give byte-identical logs | determinism | 250 |
| Every byte of every header and its stored hash, flipped, refuses to open | I6 | 40 |
| Any byte of the log, flipped, refuses or reads as a shorter log | I6 (stronger) | 20 |
| Two continuations of one prefix diverge at the first differing position | I6 | 40 |
| A checkpoint at every `P`, alone beside the log, plus the tail equals full replay at every `Q ≥ P` | I7 | 60 |

Generator coverage is counted and the model property fails if any case occurs
fewer than ten times. In one run of 1,000 scripts: redundant assert 5,275,
redundant retract 11,122, assert-then-retract of an absent quad 1,503, blank
label reused across requests 5,235, existing blank node by handle 5,065,
expected position right 2,794, stale (conflict) 925, validator reject 211,
validator attachment 709, no change 1,942, committed 6,162, clock stepping
backwards 427, checkpoint 724, settings commit 774. The first version of the
generator produced assert-then-retract **once** in 300 scripts; it was changed
to produce it deliberately.

Three deliberate mutations of the store — normalisation ignoring the head, a
delta chain that forgets a re-assertion cancels a retraction, run merging with
the older run deciding — each fail the model property. Example tests cover the
failed state, subscriptions (filtering, `Settings` and `Erasure` always
delivered, resumption, waiting at the head), the §6 comparer against a stub,
and one rule each for the dictionary, blank node scoping and handles, and
validators.

**Allocation**, measured as 500 against 4,000: a scan of a pinned read and of
an as-of read, **0 bytes per quad**; the index update, **exactly 192 bytes per
quad** — the six key arrays and nothing else; a whole commit of known terms,
**1,040 bytes per quad** against a budget of 2,048 stated before measuring.

### Shrunk counterexamples found during development

1. **Specification §6: deltas under `;` are not a monoid.** CsCheck seed
   `fIBTkyl27KJ4`. Minimised by hand to `a = (∅,{q})`, `b = (∅,{q})`,
   `c = ({q},∅)`: `(a;b);c = (∅,∅)` and `a;(b;c) = (∅,{q})`. Associativity
   holds over chains of exact deltas, which is every use a log makes of it.
   Kept as `composition_is_not_associative_over_deltas_no_log_could_hold`.
   **Reported as a proposed change below, not patched.**
2. **A recovery bug in the store.** CsCheck seed `655VFtVGHEs5`, one shrink,
   64-byte records and 1 KiB segments. A crash that left a new segment shorter
   than its 8-byte preamble was ignored on open, as it should be; recovery then
   sealed that segment and began another; and the *next* open refused the whole
   log, because the short segment was no longer the last. One crash in that
   window plus one more commit made the dataset unopenable. Fixed in
   `14502be`; kept as `a_cut_inside_a_segment_preamble_survives_the_next_reopen`,
   which fails without the fix.
3. A defect in a test, not the store: a script that commits nothing leaves no
   segment, and `Single()` threw (seed `0000GGOdfNl1`).

### Benchmarks, and ADR 0012's revisit condition

Intel Xeon @ 2.10 GHz, 4 cores, 15 GiB, 260 MiB L3, Ubuntu 24.04.4 in a cloud
container; .NET 10.0.12; BenchmarkDotNet 0.15.8. 1,000,000 generated quads
(`StoreBenchmarks.cs`). Full tables in `tests/Varve.Benchmarks/README.md`.

- **Index size**: 192 bytes per quad (six orders × 32 bytes); a checkpoint,
  dictionary included, 229; the log 69; the whole store on the heap 579.
- **Commits**: 571,000 quads/s as one commit of 100,000; 385,000 quads/s as
  commits of 1,000; 150,000 single-quad commits/s into 100,000 quads.
- **Scans**: 45 M quads/s full; 42 M on a bound predicate; 37 M on the default
  graph; 0.78 µs per subject lookup; 320 bytes per scan, none per quad.

**Verdict: ADR 0012's revisit condition does not fire.** The rejected 128-bit
content-derived ids would make the same layout 384 bytes per quad, and the scan
numbers give no reason for a wider id. The ADR's on-disk locality and
compression hypothesis is untested until milestone 6 has a layout.

### Proposed specification changes, not patched

1. **§6, delta composition.** "Deltas under `;` form a monoid with identity
   `(∅, ∅)`" is false (counterexample 1). Proposed: "`;` has identity `(∅, ∅)`
   and is associative over any sequence of deltas each exact against the state
   the previous ones produce — in particular over any run of a log." R2 and R3
   use it only that way, so nothing else changes.
2. **§4, I3, and RDF 1.2 triple terms.** "Every id in `alloc_P` occurs in `A_P`
   or `meta_P`" cannot hold literally: a triple term's components are allocated
   so that its identity can depend on theirs — a triple term around a blank node
   is a different term for each blank node — and they then occur only inside the
   triple term's entry. The store and the model both read it as *reachable* from
   `A_P` or `meta_P` through entries. Proposed: "…occurs in `A_P` or `meta_P`,
   or is a component of an entry in `alloc_P` that does."

Accepted by the maintainer during the session and now in 1.2: §8 (ADR 0046),
§10 determinism for crash-free histories, §1's named-graph scope sentence, and
§6's note on hashing.

### Other findings

- **`eng/BannedSymbols.txt` commented with `#`**, which BannedApiAnalyzers
  reads as an empty symbol entry: harmless alone, RS0031 as soon as a second
  list does the same. Both lists now use `//`.
- **`Varve.Rdf` and `Varve.Turtle`'s READMEs said Apache-2.0**, and are packed
  into their packages, two milestones after ADR 0031. Corrected.
- **A limit stated, not solved** (ADR 0044): a request cannot build a new
  triple term around an existing blank node.
- **Not built, as the milestone said**: refusing to continue from a head that is
  not the store's own (replication), archive, erasure mode, the file backend.
- **Merge note**: `eng/changelog-sections.txt` starts milestone 4 at `2a37398`.
  A squash merge would change that hash; a merge commit keeps it.

### What milestone 5 needs from the maintainer

1. **ADR 0003's open question 2** — the optimiser and evaluator both at layer 3.
   Due at milestone 5, and it decides the package list before code.
2. **A benchmark plan for ADR 0022's revisit condition.** The store's inline
   integers carry their values in the handle, but the layout is deliberately
   private, so today a numeric `FILTER` externalises. 0022 names a typed-value
   accessor beside the handle as the likely successor; whether to design it in
   milestone 5 or first measure without it is the maintainer's call.
3. **Whether `IQuadSource` gains a cardinality estimate** for the optimiser.
   A layer 1 contract change, so an ADR, and `InMemoryDataset`, `QuadOverlay`
   and `DatasetView` all implement it.
4. **The two proposed specification changes** above.
5. **`Varve.Xsd`'s scope**: value equality belongs to the evaluator (ADR 0022,
   0024), so `Varve.Xsd` is lexical-to-value mapping and canonical forms. The
   store's inline rule already carries a private canonical-integer check that
   `Varve.Xsd` should replace — the store sits at layer 4 and may reference it.
6. **`DatasetView`'s lifetime under a query**: a pin is an engine snapshot for
   one operation (ADR 0015); the evaluator's API should make that operation's
   boundary explicit rather than hold a view for a session.

## Commits

```
2a37398  docs(adr): 0040–0045, what milestone 4 decides that set zero did not
646e439  docs(spec): version 1.2 — settings reach every subscriber, and three clarifications
80e612a  docs(adr): 0034 amended — a branch-bound cloud session lands through a pull request
38766d7  feat(rdf): QuadDelta and QuadOverlay — the delta and the overlay at layer 1
c9f1b7e  build: ambient clock and randomness banned where log bytes are compared
7042ad7  feat(store): Varve.Store — the log, the sequencer and the default projection
6771c6a  test(store): the reference model, and the property that ties §10 together
14502be  fix(store): a cut inside a segment's preamble no longer bricks the next open
94dbe65  test(store): I7 for every position, the failed state, subscriptions, and the rules one by one
9febaf1  test(store): allocation — zero per quad on a scan, the six key arrays on an index update
9930a37  test: the store under Native AOT and in the browser
cba409a  perf(store): commit and scan throughput, and index size, against ADR 0012
eaaf665  docs: milestone 4 — the state, the roadmap, and the revisit conditions
```

## What this record does not contain

The transcript, which the maintainer holds, and therefore the order in which
things were tried within each commit. The commits are one per step as the
prompt asked, with one honest exception: the store's first commit (`7042ad7`)
carries the log, the sequencer, reads, checkpoints and subscriptions together,
because they were written as one design before the first test ran. The tests
that follow it are split by property.
