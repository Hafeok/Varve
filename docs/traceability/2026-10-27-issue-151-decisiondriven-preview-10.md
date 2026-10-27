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
