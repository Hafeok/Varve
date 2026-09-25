# 0027 — Benchmarking

## Status

**Accepted.** 2026-09-21.

## Context

`docs/brief.md`: "Performance claims need BenchmarkDotNet numbers and a stated
dataset. Compare against Oxigraph and dotNetRDF on the same hardware."

That is a mandate for a specific package, and it collides with something this
repository decided about packages. **ADR 0009 refuses any package that ships a
native asset** — "a property of the package, not of how we intend to use it,
and there is no exception process". BenchmarkDotNet 0.15.8 depends on
`Gee.External.Capstone`, which ships nine
`runtimes/*/native/libcapstone.so` entries (verified 2026-09-21 by inspecting
the package). `Iced`, its other disassembly dependency, ships none.

So the brief requires a package that ADR 0009 as written forbids. One of them
has to move.

## Decision

### The native-asset ban is scoped to shipped artifacts

**ADR 0009 is amended, with the amendment recorded in that ADR and dated.** The
refusal binds the **runtime class** — anything that can reach a published Varve
package — and does not bind build-time or test-only packages that never leave
the machine.

The reasoning: constraint 1 is "100% managed code. No P/Invoke, no native
binaries". It describes **Varve** — the thing a consumer installs, trims,
compiles ahead of time and runs in a browser. A benchmark harness is none of
those things. It is not published, not referenced by anything published, and
not present in any artifact a user receives. Refusing it would enforce
constraint 1 in a place constraint 1 was never about, at the cost of the
measurement discipline the same brief requires.

The ban is unchanged where it matters, and it was my wording rather than the
brief's that made it absolute.

### BenchmarkDotNet 0.15.8

MIT. Fifteen dependencies, which is a large tail for one package and is the
honest cost:

- `Gee.External.Capstone` (**native**, disassembly), `Iced` (managed,
  disassembly), `Microsoft.Diagnostics.Runtime`,
  `Microsoft.Diagnostics.Tracing.TraceEvent`, `Perfolizer`, `CommandLineParser`,
  `System.Management`, `Microsoft.Win32.Registry`, and — notably —
  **`Microsoft.CodeAnalysis.CSharp`** and **`System.Reflection.Emit`**.

Two of those deserve naming rather than burying:

- **`Microsoft.CodeAnalysis.CSharp`** is in the register at the **5.0.0 Roslyn
  floor** (ADR 0009), and central transitive pinning applies it here too. If
  BenchmarkDotNet ever needs a newer Roslyn than the analyzer floor allows,
  that is a conflict to resolve deliberately rather than by raising the floor.
- **`System.Reflection.Emit`** is on `eng/BannedSymbols.txt` — banned for
  packable projects, which a benchmark project is not. This is the one place in
  the repository where runtime code generation legitimately appears, and it is
  worth being explicit that the ban is not being weakened: the benchmark
  project is not packable, so the banned-symbol analyzer does not run on it at
  all.

The benchmark project declares `VarveLayer=none`, `IsPackable=false`, and is
**not in CI**. Benchmarks on a shared runner measure the runner.

### `dotNetRdf.Core` gains a second, independent justification

ADR 0007 admitted `dotNetRdf.Core` to read the W3C manifests, with a stated
exit criterion: removed when `Varve.Turtle` passes `rdf/rdf11/rdf-turtle` at
milestone 5, and "if this line is still here after milestone 5, that is a
defect".

**It is now also the benchmark baseline**, which the brief names explicitly.
That is a different use with a different lifetime, so:

- ADR 0007's criterion **narrows to the manifest reader's use of it**. When
  `Varve.Turtle` passes the Turtle suite, `ManifestReader` stops using
  dotNetRDF, and that is the defect-if-not-done.
- The package stays in the register as the benchmark baseline, and **the
  register citation moves from 0007 to this ADR at milestone 5**.
- ADR 0007 is not edited. Its criterion is correct about the thing it was
  about.

A baseline you no longer depend on is the only kind worth having: after
milestone 5 dotNetRDF is in this repository for exactly one reason, to be
measured against.

> **Happened 2026-09-22, at milestone 3b rather than 5.** `ManifestReader`
> reads with `Varve.Turtle`, the package reference left
> `Varve.Conformance.Tests`, and the register citation moved from 0007 to this
> ADR. The paragraph above is therefore describing the present and not a plan:
> dotNetRDF is here to be measured against, and nothing in this repository
> depends on it for an answer.

> **Amended 2026-09-25** (milestone 5b). dotNetRDF gains one **offline** use,
> decided by the maintainer on the 5b plan: it generated, once, the N-Triples
> translations of the W3C SPARQL suites' RDF/XML files (the `sort` results and
> the `subquery` data), committed under `tests/fixtures/w3c-rdfxml/` with the
> SHA-256 of each original. Nothing reads dotNetRDF at test time — the
> conformance harness reads the committed N-Triples, and a guard test fails if
> a submodule bump changes an original's hash. The generator is the benchmark
> project's `convert-rdfxml` command, so the dependency stays where this ADR
> admitted it. The fixtures are deleted when `Varve.RdfXml` passes its own
> suite, at which point the harness reads the originals. This is a second
> reason dotNetRDF answers something, and it is stated rather than implied:
> the translations are a third party's reading of RDF/XML, which is what an
> independent fixture should be.

## Alternatives considered

- **Keep ADR 0009 absolute and suppress the asset flow** — a direct
  `Gee.External.Capstone` reference with `ExcludeAssets="all"`. Honest about
  intent and cosmetic in effect: the package still restores and is still in the
  graph, so the ban as written is still violated. It would have replaced a
  stated exception with a pretence of compliance.
- **Keep ADR 0009 absolute and hand-roll a harness.** No new packages at all.
  Lost to the brief, which names BenchmarkDotNet, and to the measurement
  itself: a hand-rolled harness gets warm-up, iteration counts, outlier
  handling and statistical reporting wrong in ways that produce confident wrong
  numbers — which is worse than no numbers.
- **Supersede ADR 0007** to carry both the manifest criterion and the benchmark
  baseline. Cleanest under ADR 0001's no-edit rule. Lost on proportion: a
  supersession whose only change is one dependency's lifetime buries a decision
  that is otherwise entirely correct.
- **Benchmark Varve alone**, with no comparison. Keeps 0007 untouched and lets
  dotNetRDF leave at milestone 5. Lost because a throughput number with nothing
  to compare it against is not a performance claim, it is a measurement.
- **Compare against Oxigraph now.** The brief asks for it and it is the more
  interesting comparison. Deferred deliberately: it needs a native process, a
  harness that drives it fairly, and a dataset large enough that process startup
  is noise. That is its own piece of work and it is not milestone 3a's.

## Consequences

**ADR 0009's ban now has a boundary, and boundaries get tested.** The rule is
"can this reach a published artifact" and the answer for a test or benchmark
project is no. The place this could erode is a test-only package that a
packable project ends up referencing; the layering rules and `IsPackable`
already make that visible, and the register records the class of every package.

**Benchmarks are not a gate and are not in CI.** Numbers come from a stated
machine with stated hardware, reported alongside the dataset that produced
them. A number without its hardware is not a number.

**The dataset must be reproducible.** A generated N-Triples file from a stated
seed and generator, not a downloaded corpus that may change — otherwise two
runs a month apart are not comparable and the whole exercise is decorative.

**Oxigraph remains owed.** The brief asks for it; this ADR defers it and says
so rather than quietly dropping it.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0020–0026).
  Touches **0009** (amended, dated, in that file), **0007** (criterion narrowed
  by this ADR, 0007 itself unedited), and **0004** (the `System.Reflection.Emit`
  ban is unweakened; a benchmark project is not packable, so the banned-symbol
  analyzer does not run on it). No conflict with any.
- **Layer ownership.** None. The benchmark project declares `VarveLayer=none`
  and is not packable.
- **Analyzer rule.** None.
- **Open questions owned.** None. The Oxigraph comparison is deferred work, not
  an open question — it has an answer, and nobody has done it.
