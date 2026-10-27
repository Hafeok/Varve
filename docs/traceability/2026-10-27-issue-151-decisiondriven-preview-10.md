# DecisionDriven 0.1.0-preview.10, and DD0019's memoised fields

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#151](https://github.com/Hafeok/Varve/pull/151) and [#122](https://github.com/Hafeok/Varve/pull/122), the dependency bumps this supersedes; referenced, not closed |
| **Date** | 2026-10-27 |
| **Tool** | Claude Code 2.1.311, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS5no4fFRiixjLxkxT` |
| **Branch** | `claude/awesome-hamilton-6pcks7`, from `main` at 6cdb402 |

## The prompt

> Small PR, no descriptor. Commits reference #151 and #122. Bump DecisionDriven.Analyzers and DecisionDriven.Report to <the new preview> (the version in `Directory.Packages.props` and `.config/dotnet-tools.json`). Never suppress DD0019.
>
> 1. **`[Memoized]` on the caches of a pure function:** `RdfTerm._hash`, `ValidationReport._canonical`, and `Commit._carried` if it holds an immutable value. Shape each write to the accepted idiom.
> 2. **`GraphScopedQuadSource._decided`** becomes `readonly ConcurrentDictionary<TermHandle, bool>`, used only through `GetOrAdd` with a static factory, and marked `[Memoized]`. That fixes the concurrent-write race and keeps ADR 0107's `PrefixDecisionMemoisedPerGraph`. Add a test: many threads reading one scoped source over prefixed graphs give the same answers as one thread. Keep the allocation test green, and say if `GetOrAdd` costs an allocation on the hot path.
> 3. **`ValidationReport._graph`:** if `ToDataset` hands out the cached dataset itself and it can be mutated, return an immutable view or a fresh copy instead. Only an immutable cached value may be `[Memoized]`. Check `Commit._carried` the same way.
> 4. **`CommitRequest`:** read DD0019's full message for the "mark it" option. If it declares a type mutable by design and never handed out as a value, use it. Otherwise rename the type to `CommitRequestBuilder` (breaking before 1.0; update the public API baseline). Say which, and why.
> 5. **Rerun** the parse benchmarks (no regression expected) and the full build against the new preview, and confirm no `DD` finding remains in any project.

## The report

### The version

`<the new preview>` was resolved from nuget.org, not memory: the newest
published version of both packages is **0.1.0-preview.10**, and it is the
first whose README names `[Memoized]` (preview.9's does not). Both
`Directory.Packages.props` entries and the tool manifest moved from
preview.7.

### What preview.10 reported, and the answers

The build against preview.10 reported DD0019 in three projects, and nothing
else. No finding was suppressed, and no `[DesignDecision]` was added.

- **`RdfTerm._hash`** — marked; the write is `if (_hash == 0) _hash =
  ComputeHash();`, zero the sentinel and a computed zero stored as one, as
  before.
- **`GraphScopedQuadSource._decided`** — a `readonly
  ConcurrentDictionary<TermHandle, bool>`, marked, filled only by
  `GetOrAdd(graph, static (handle, self) => self.Decide(handle), this)`.
  The rule also reported the three `_decided is null` tests as handing the
  dictionary out; whether the scope decides by prefix is now a readonly
  `_byPrefix` flag, which reads better anyway. That report on a null test is
  arguably a false positive of the analyzer; it is noted here for an upstream
  issue rather than worked around in the rule's place.
- **`ValidationReport._graph`** — `ToDataset` already built a fresh
  `InMemoryDataset` from the cached triples on every call, so it never handed
  out anything cached. The cache itself, `ReportGraph`, held a `List`; it now
  holds an `ImmutableArray`. DD0019 then reported that `_graph` was computed
  from `this`, so `ReportGraph.Build` takes the report's get-only values
  instead of the report.
- **`ValidationReport._canonical`** — was a `byte[]`, a mutable type; it is
  an `ImmutableArray<byte>` (wrapping the encoder's array without a copy),
  written by `if (_canonical == default)` and handed out as
  `ReadOnlyMemory<byte>`, as before.
- **`Commit._carried`** — was a `HashSet<ulong>`, mutable, though never
  handed out. It is a `FrozenSet<ulong>`, so the memoised value is
  immutable, written by `_carried ??= Carried()`. It costs one more copy
  once per commit, the first time a handle outside its allocations is
  externalised, and its lookups are no slower.
- **`CommitRequest`** — renamed `CommitRequestBuilder`. DD0019's message
  offers `[DesignDecision(typeof(<Set>.<Key>), Scope = …)]` "citing the
  accepted decision that says so". That accepts a violation under a filed
  and accepted decision; it does not declare a type mutable by design, no
  accepted decision covers this type, and a session cannot accept one. The
  rule's own exemption for a type mutable by design is a sealed `*Builder`,
  which the type is: its caller assembles it and passes it once to
  `CommitAsync`. Breaking before 1.0; `PublicAPI.Unshipped.txt` and the
  package README move with it.

### What else names `CommitRequest`, and stays

ADRs, the accepted decision sets `wrapper-types-and-the-store-log-namespace`,
`store-log-surfaces` and `the-insert-data-fast-path`, the accepted
`docs/spec/sparql-update-store.md`, the CHANGELOG and the release descriptors
keep the old name: each is accepted or historical, and changes only by its
own amendment.

### Tests and allocation

The new test reads one scoped source from sixteen threads at once, over
2,000 prefixed graphs each decided for the first time, twenty rounds, and
compares with one thread. Against the old `Dictionary` it failed three runs
in three (a corrupted-collection exception); against the new one it passes.
A second test asserts that a made decision is read with zero bytes
allocated: `GetOrAdd` with a static lambda and its argument passed through
allocates no closure or delegate per call, and a hit is a read. The protocol
allocation test stays green.

### The pipeline

`dotnet run eng/ci.cs` passed every job. The first run stopped at
`test-sparql-results` because the container had not checked out the W3C
submodules; after `git submodule update --init --recursive`, that job and
every later one passed, conformance and pack included. The Release build
reports no warning and no error in any project, so no `DD` finding remains.
`decision-report` does not gate. Its baseline was already stale before this
change, from preview.7 and missing several packages, and is left for its own
refresh.

### The parse benchmarks

The machine was this cloud container, .NET 10.0.14, x86-64-v4, running
BenchmarkDotNet in process with `--warmupCount 3 --iterationCount 8
--invocationCount 1 --unrollFactor 1`, at 6cdb402 and then this branch.
Allocation was the same to the byte in every Varve row, for N-Quads and for
Turtle. Means moved within the run's noise; dotNetRDF itself moved by up to
15% between the runs. The rows that intern and hash terms, into
`InMemoryDataset`, were 252.5 → 231.5 ms (N-Quads) and 98.0 → 85.7 ms
(Turtle). There is no regression.

## The second prompt

> Yes, open the PR. Six points:
>
> 1. #151. That’s Dependabot’s 12-package bump. Either take all 12 into this branch, in one commit that says so, or rebase onto #151 after it merges. Say in the PR which you did. If you take them, I’ll close #151 as superseded.
> 2. The rename to `CommitRequestBuilder`: agreed. The rule’s own exemption is the honest answer, and `[DesignDecision]` would have accepted a violation. Don’t leave the accepted documents saying something the code no longer does. Add a dated amendment, unaccepted, to `docs/spec/sparql-update-store.md` and to each ADR and decision set that names `CommitRequest`, recording the rename and its reason. It will be red on `CS0618` until I accept it on the PR, which is expected. Add a CHANGELOG entry marking it breaking (ADR 0118).
> 3. `Commit._carried` as a `FrozenSet`. `FrozenSet` is built for fast reads and is slower to build than a `HashSet`. A copy per commit on the commit path needs a number. Add the `INSERT DATA` benchmark (#35’s) and a commit-path row, old head against this branch. If it regresses past noise, use an `ImmutableHashSet`, or keep a private `HashSet` that nothing hands out and mark it `[Memoized]` with that justification.
> 4. The DD0019 false positive. Draft the upstream issue in the PR body:
>    * a minimal repro: a field checked with `is null` and never returned, reported as handed out;
>    * generic names only. The analyzer repo never names Varve.
>  I’ll file it.
> 5. The decision-report baseline. Regenerate it in a separate small PR right after this one lands. Don’t leave it at preview.7: bumping the analyzers is exactly what makes it stale.
> 6. No release descriptor of its own. It rides the next release, and lands through `land/` as usual.

## The second report

1. **#151 is taken whole.** One commit bumps its ten packages that are not
   DecisionDriven, at its versions, each checked on nuget.org. Its other two
   are already at preview.10 here, newer than its preview.9. The same commit
   carries what preview.10 changes against preview.7, from the package README,
   because no release notes are published (ADR 0063).
2. **The amendments.** ADRs 0058, 0065 and 0120 and the SPARQL Update
   specification each gain a dated amendment, named on their Status lines. The
   decision sets of 0065, 0120 and `store-log-surfaces` are amended too, all
   unaccepted. `LogTypesThatMove` and
   `DataOnlyRequestsGoStraightToTheCommitRequest` name the new type with no
   acceptance; the second keeps its key. **Neither key is cited in code**, so
   neither raises a `CS0618`; the `CS0618` this pull request does raise is the
   decision under point 3. **No CHANGELOG entry**, because `CHANGELOG.md` is
   the projection of `releases/`, and `eng/changelog.cs --check`, a gate, fails
   on any hand edit (ADR 0102). The rename's commit is `refactor(store)!:`, so
   `--unreleased` already lists it as **(breaking)**. ADR 0118 puts breaking
   changes in the release descriptor's summary, which the next release
   writes.
3. **`Commit._carried`.** `CommitPathBenchmarks` is new: `Commit` is
   `CommitAsync` of N quads, and `FirstExternaliseOfACarriedHandle` is the one
   place the set is built. The machine was this container, .NET 10.0.14,
   BenchmarkDotNet in process, 6cdb402 against this branch.

   | Row | N | 6cdb402 | FrozenSet branch | Allocated (both) |
   |---|---:|---:|---:|---:|
   | `UpdateBenchmarks.Varve` (INSERT DATA, #35) | 10,000 | 48.5 ms | 61.0 ms | 28.89 MB |
   | | 100,000 | 508.8 ms | 512.0 ms | 258.9 MB |
   | `CommitPathBenchmarks.Commit` | 10,000 | 21.8 ms | 53.3 ms ± 22.1 | 24.15 MB |
   | | 100,000 | 334.8 ms | 245.6 ms | 230.2 MB |

   The commit path and `INSERT DATA` allocate the same bytes. Their times
   move both ways inside their deviations, as they would if nothing on them
   changed, and nothing does: the set is not built there. The build itself
   regressed past noise, so all four sets were measured in one later run:

   | Set, 100,000 quads | Build and one lookup | Allocated |
   |---|---:|---:|
   | `HashSet` (6cdb402) | 4.4 ms | 11.95 MB |
   | `FrozenSet` | 10.7 ms | 22.77 MB |
   | `ImmutableHashSet` | 37.7 ms | 10.69 MB |
   | sorted `ImmutableArray`, binary search | 15.5 ms | 3.13 MB |

   `ImmutableHashSet` is the worst of the four. preview.10 refuses a
   `[Memoized]` `HashSet` as such: "a plain collection filled by concurrent
   readers is a race". A justification therefore has to be a decision that
   `[DesignDecision]` cites. It is filed as
   `StoreLogSurfaces.CarriedHandlesAreAPrivateHashSet`, scope `HotPath`,
   unaccepted, and its citation is the `CS0618` above. The sorted array is the
   alternative if the maintainer would rather not accept it: immutable, no
   decision, 3.5 times the build time and a quarter of the memory.
4. **The upstream issue** is drafted in the pull request body, from a
   standalone project that builds against preview.10 with only DD0019
   reported: a `[Memoized]` `ConcurrentDictionary` tested with `is not null`
   and never returned, reported as handed out, and the same field without the
   null test not reported.
5. **The baseline** is for the next pull request, once this one lands.
6. **No descriptor.**

## The third prompt

> Agreed on the CHANGELOG. You’re right, and the next descriptor carries the rename as breaking.
>
> _carried: keep the HashSet under CarriedHandlesAreAPrivateHashSet. I’ll accept it on the PR. Tighten its statement to the publication rule: “Built completely in a local, published once with Interlocked.CompareExchange, never mutated or handed out after publication.” Make the code match if it currently uses ??=. Keep the build-time table in the decision’s context, with the sorted ImmutableArray as the rejected alternative: O(log n) on every lookup, against a build that is rare.
>
> I’ll close #151 when #173 lands. Upstream filing is mine.

## The third report

The statement is the maintainer's, word for word. The code used `??=`, so
`Commit` now reads `_carried` with `Volatile.Read` and publishes with
`Interlocked.CompareExchange`. A reader that loses the race takes the
published set. preview.10 reports nothing on that write; the only finding is
the decision's `CS0618`. The decision's context keeps the build-time table
and records the sorted `ImmutableArray` as rejected. A new test,
`many_threads_externalising_one_commit_answer_what_one_thread_answers`, asks
one fresh commit from sixteen threads at once over 6,000 handles, ten
rounds, and compares the answers with one thread's. It guards the rule. It
is not a reproduction: the `??=` form also answered correctly, because each
racing reader built a complete set of its own.
