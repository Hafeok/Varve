# `DD0019` under DecisionDriven.Analyzers 0.1.0-preview.9: stopped and reported

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The request is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#151](https://github.com/Hafeok/Varve/pull/151), the analyzer bump; and [#122](https://github.com/Hafeok/Varve/pull/122), the earlier bump it replaces; referenced, not closed |
| **Date** | 2026-10-26 |
| **Tool** | Claude Code, a cloud session |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01NDSkkZAwMGEAPjqeAAoVdm` |
| **Branch** | `claude/sweet-hamilton-9a6e6q`, from `main` at d77fa5e |

## The request

> Small PR, no release descriptor. Commits reference #151 and #122, with no closing keyword. DecisionDriven.Analyzers 0.1.0-preview.9 (#151) reports two `DD0019` findings in `Varve.Rdf`, and both are real. Never suppress `DD0019`.
>
> 1. **`RdfTerm.GetHashCode` writes `_hash`.** Compute the hash in the constructor and make the field `readonly`. Measure N-Triples and Turtle parse throughput before and after with the existing benchmarks. If it regresses by more than 2%, stop and report.
> 2. **`GraphScopedQuadSource` writes `_decided` on the read path** (`Allows` → `ByPrefix`). That's also a data race: concurrent readers of one scoped source write one `Dictionary`.
>    - At construction, decide prefix and governor membership for every graph the inner source knows, as the explicit graphs already are, and keep the result in a `readonly` set.
>    - A graph met later that wasn't known at construction (a pending graph in an overlay, say) is decided per call with no memo.
>    - Keep the allocation test green, and add a concurrency test: many threads reading one scoped source over prefixed graphs get the same answers as a single thread.
>    - Report the cost of the eager decision on a dataset with many named graphs, e.g. REC M's per-site graphs.
> 3. **Check against preview.9.** Build with #151's package versions locally to confirm no other `DD` finding remains, and list any that do.
>
> Once this lands, #151 rebases and should be green.

## What was done

Nothing in `src/` changed. Both code items stopped, one on the request's own
threshold and one on the maintainer's answer to a question, so this record is
the session's only commit.

### 1. `RdfTerm`: stopped, regression past 2%

The hash was computed in the constructor into a `readonly` field, the same
function as before; preview.9 then reports nothing on `RdfTerm`. Measured with
`ParseBenchmarks` (100,000 N-Quads, the line-based reader N-Triples shares)
and `TurtleBenchmarks`, the Varve rows only, `--inProcess` (the out-of-process
toolchain failed to build its boilerplate in a worktree), two runs before and
two after, alternated. Machine: Intel Xeon 2.10 GHz, 4 cores, a cloud
container; .NET SDK 10.0.401, runtime 10.0.12.

| Row | Before (run 1, run 2) | After (run 1, run 2) |
|---|---|---|
| N-Quads — views | 107.6, 122.7 ms | 108.2, 103.2 ms |
| N-Quads — owned terms | 122.8, 132.8 ms | 135.4, 127.6 ms |
| N-Quads — into InMemoryDataset | 302.7, 307.4 ms | 320.0, 228.9 ms |
| Turtle — views | 26.2, 26.6 ms | 27.2, 26.1 ms |
| Turtle — owned terms | 38.2, 38.4 ms | 48.0, 46.7 ms |
| Turtle — into InMemoryDataset | 113.7, 109.1 ms | 119.6, 126.6 ms |
| Turtle — read and write back | 48.7, 46.2 ms | 50.2, 48.8 ms |

The views rows build no `RdfTerm` and do not move. Turtle's owned terms is
about 23% slower in both runs, and Turtle into a dataset 5–16%: every
materialised term now pays a hash whether anything hashes it or not. The
N-Quads rows are inside this container's noise (standard deviation about 10%
of the mean), which cannot resolve 2% either way. Not committed.

### 2. `GraphScopedQuadSource`: stopped by the maintainer

`IQuadSource` (ADR 0022) has no way to list a source's graphs. The only one is
`Match(None, None, None, AnyNamed)` over every named quad, which is how the
evaluator answers `GRAPH ?g {}` (`Exec.cs`). A scoped source is built per
scoped read, and `DataGraphSource` builds one per commit under a shapes
binding, so deciding eagerly at construction would cost a scan of the named
quads each time: on REC M (622 site graphs, roughly 2M quads at the head) a
full scan per read and per gated commit, not a decision per graph. A graph
listing on the contract would make it O(graphs), and needs a dated amendment
to ADR 0022 and to ADR 0107. The change also replaces two accepted rulings of
0107, `PrefixDecisionMemoisedPerGraph` and `GoverningGraphSeam`, which both say
the decision is memoised per graph handle. Asked which way to go, the
maintainer answered: stop and report only.

### 3. preview.9 across the solution

`DD0019` is an error whatever `TreatWarningsAsErrors` says, so a finding in a
project hides every project above it. Every project was first built with
preview.7, then rebuilt with preview.9 restored and
`-p:BuildProjectReferences=false`, so that each is analysed against its
references' preview.7 outputs. 50 of 53 projects build; the three that fail
hold nine findings, all `DD0019`:

| Project | Where | Member | Writes |
|---|---|---|---|
| `Varve.Rdf` | `RdfTerm.cs:239` | `GetHashCode` | `_hash` |
| `Varve.Rdf` | `GraphScopedQuadSource.cs:143` | `Allows` → `ByPrefix` | `_decided[]` |
| `Varve.Shacl` | `Model/ValidationReport.cs:70` | `ToDataset` → `Graph` | `_graph` |
| `Varve.Shacl` | `Model/ValidationReport.cs:77` | `ToCanonicalNQuads` | `_canonical` |
| `Varve.Store` | `Log/Commit.cs:90` | `TryExternalise` → `Carried` | `_carried` |
| `Varve.Store` | `Log/CommitRequest.cs:58, 62` | `Assert` → `Add` | `_operations.Add` |
| `Varve.Store` | `Log/CommitRequest.cs:66, 70` | `Retract` → `Add` | `_operations.Add` |

No other `DD` id reports. #151 is not green after the two `Varve.Rdf`
findings alone.

## What was decided by the session, and is open to the maintainer

- The measurement used `ParseBenchmarks` for N-Triples: there is no
  N-Triples benchmark, and N-Quads is the same line-based reader.
- An alternative for item 1 that was not built or measured: drop the cache and
  compute the hash on every call. The owned-terms path would then pay nothing,
  and an interning table hashes each term about once anyway.
