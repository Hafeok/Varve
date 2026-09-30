---
set: layer-6-host-state
namespace: varve
origin: "DD0004 findings on the layer 6 hosts in session 3 of #43, reached once DecisionDriven 0.1.0-preview.6 let the build past Varve.Xsd"
decisions:
  - key: BenchmarkSinkIsStatic
    statement: "A parse benchmark's sink, the counter its callbacks add to so that the parse is not optimised away, is a static field, because the callbacks are static lambdas and an instance sink would make each one a closure: an allocation in the arm that measures allocation"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-30T00:00:00Z
  - key: EvaluationRunnerReadsEachFileOnce
    statement: "The evaluation-suite runner that the conformance tests and the benchmarks share keeps what it has read of each data file in one static cache for the process, because every case of every suite loads through it and the suites' files do not change during a run"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-30T00:00:00Z
---

# What the layer 6 hosts keep in static state

**Unaccepted.** Filed by session 3 of #43, for the maintainer.

Until `DecisionDriven.Analyzers` 0.1.0-preview.6, every build stopped in
`Varve.Xsd`, so no build reached the hosts at layer 6 (ADR 0060): the
benchmarks, the AOT smoke app and the browser smoke app. The first build that
did reported 24 `DD0004` findings. Most were answered by the design change the
rule asks for:

- **Fixed tables** — the BSBM queries and words, the suite and directory lists,
  the update sizes, the pinned browser capabilities — are `ImmutableArray`s.
- **The generated datasets** are `ImmutableArray<byte>` over the array they were
  built in, with no copy. BSBM's is held by a nested class, so naming the
  queries still does not generate it.
- **The evaluation catalogue** exposes an `ImmutableArray` and a
  `FrozenDictionary`, and its lazily read entries hold those.
- **The SPARQL benchmark's sink** is added to from an instance method, so it is
  an instance field.
- **The AOT smoke app's counter** is a `Tally` made per check: the app
  measures nothing, so the delegate over it may allocate.
- **The browser smoke app's probe results** are a dictionary made in `Run` and
  passed to the probes and to the check that reads them.
- **`BsbmBenchmarks.Names`** is a method. As a property it was reported as a
  static registry, though it computes its sequence on every read and stores
  nothing; that report is Hafeok/decision-driven-analyzers#80.

Two are not answered by a change, and are filed here.

**`BenchmarkSinkIsStatic`.** `ParseBenchmarks` and `TurtleBenchmarks` hand the
parser `static (in QuadView quad) => sink += …`. A static lambda is cached
once, so the "views" arms show what the parser allocates and nothing else;
that zero is the number those arms exist to report. An instance sink needs
`this`, which makes each lambda a closure allocated per invocation. Both
fields cite this with `Scope = ExceptionScope.HotPath`. The alternative is an
instance sink, and a note beside each figure that it includes the closure.

**`EvaluationRunnerReadsEachFileOnce`.** `EvaluationData` is linked into the
benchmarks from the conformance tests. Its `Cache` maps a data file to the
quads read from it, so that the thousands of cases that share a file parse it
once per process, not once per case. It cites this with
`Scope = ExceptionScope.Pool`. The alternative is a cache owned by an object
the runner is given, which means threading it through every xunit theory's
member data and every benchmark's setup.
