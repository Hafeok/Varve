# 0056 — Evaluator options: extension functions, the clock and randomness are injected

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the milestone 5b plan.
Extends ADR 0011's ban on ambient time and randomness from `Varve.Store` to
`Varve.Sparql.Evaluation`.

## Context

Four of SPARQL's functions read the world: `NOW()` (§17.4.5.1) the time the
query runs, `RAND()` (§17.4.4.5) a pseudo-random number, and `UUID()` and
`STRUUID()` (§17.4.2.12, §17.4.2.13) fresh identifiers, which RFC 4122's
version 4 makes random. Two more things the evaluator needs from outside are
extension functions — §17.6's "extensible value testing", an IRI in function
position — and custom aggregates (ADR 0053).

The repository has settled positions on both kinds. **No static
registries** (brief, principle 2; ADR 0003's reserved `VARVE0005`): a process-
wide table of functions keyed by IRI is shared mutable state that two hosts
in one process would fight over, and that a browser build would carry whether
or not anything registered. **No ambient clock or randomness** in the
packages whose output must be reproducible (ADR 0011): `Varve.Store` opts
into `eng/BannedSymbols.Deterministic.txt` and takes a `TimeProvider`. A
query's results are not written to the log, but they are compared — by the
optimiser's equivalence property, by the store-versus-dataset property, by
the differential run — and a comparison that depends on the wall clock or on
an unseeded generator is a comparison that flakes.

## Decision

**Everything the evaluator needs from outside arrives in `EvaluationOptions`**,
an immutable options object the caller constructs and passes to the
evaluator.

- **Extension functions** are an `IReadOnlyDictionary<string,
  IExtensionFunction>` keyed by the function's IRI. An `IExtensionFunction`
  receives its evaluated arguments as `RdfTerm`s and returns a term or
  reports an error, which the evaluator treats as any other expression error
  (§17.2: an error in a `FILTER` is false, in a `BIND` leaves the variable
  unbound). A `CustomFunctionCall` whose IRI is neither an XSD constructor
  (§17.5, built in) nor a key of the dictionary evaluates to an error: the
  invocation step of §17.2.1 fails, and "if any of these steps fails, the
  invocation generates an error", whose effect §17.2 defines. The query does
  not fail. Oxigraph 0.5.11 fails the whole query instead ("The custom
  function … is not supported"); the text is not silent, so it is followed
  (ADR 0038, D1). Arguments that are themselves errors are errors before the
  function is called, as for every function in §17.4 that is not a
  functional form.
- **Custom aggregates** are an `IReadOnlyDictionary<string,
  IExtensionAggregate>`, per ADR 0053, and an unknown one fails the query
  when it is compiled, because an aggregate has no per-solution error to
  degrade to.
- **The clock** is `EvaluationOptions.Clock`, a `TimeProvider`. `NOW()` reads
  it **once per execution**, when the evaluator starts, so every `NOW()` in
  one query returns the same value, as §17.4.5.1 requires ("All calls to this
  function in any one query execution must return the same value"). The instant is
  given the timezone offset the provider reports as its local zone.
- **Randomness** is `EvaluationOptions.Randomness`, an `IRandomSource`
  defined in this package with one member,
  `void NextBytes(Span<byte> destination)`. `System.Random` is banned as a
  type (it cannot appear in a signature, not only in a call), so the contract
  is our own and the caller adapts whatever generator it uses in one line.
  `RAND()` takes 53 bits of it for a double in `[0, 1)`; `UUID()` and
  `STRUUID()` take 16 bytes and set the version and variant bits of RFC 4122
  §4.4.
- **No defaults for either.** A query that calls `NOW()` with no clock, or
  `RAND()`, `UUID()` or `STRUUID()` with no randomness, fails with a
  `QueryEvaluationException` whose message names the option to set. The
  documentation shows the caller's one line — `Clock = TimeProvider.System`
  — and an adapter over `RandomNumberGenerator` for randomness. The evaluator
  never chooses the system clock or a generator on the caller's behalf.
- **The ban is extended.** `Varve.Sparql.Evaluation` sets
  `VarveDeterministic`, so `eng/BannedSymbols.Deterministic.txt` applies to
  it as it applies to `Varve.Store`: `DateTime.Now`, `DateTimeOffset.UtcNow`,
  `TimeProvider.System`, `Stopwatch`, `Environment.TickCount`, `Random`,
  `RandomNumberGenerator` and `Guid.NewGuid` are compile errors in the
  package. The list's messages are reworded so that they name the injected
  option generally rather than the store's `DatasetOptions.Clock` alone.

## Alternatives considered

- **A registry of extension functions**, static or per process. The ordinary
  shape in other engines. Rejected by principle 2 and ADR 0003: shared
  mutable state, and a browser build that carries it regardless.
- **`TimeProvider.System` and a seeded generator as defaults**, documented.
  One less line for the caller. Rejected by the maintainer on the plan: a
  default is the evaluator choosing an ambient clock for the caller, which is
  exactly the dependency the ban exists to make visible, and a query that
  "works" in development and flakes in a comparison is worse than one that
  says which option it needs.
- **A deterministic built-in generator** (for example seeded from the query
  text). Reproducible. Rejected: `UUID()` is required to return a *fresh*
  identifier per call (§17.4.2.12), and two executions of one query returning
  the same fresh identifiers is not fresh.
- **`System.Random` in the options** instead of a new interface. No new type.
  Rejected: the ban is on the type, deliberately, and carving out an
  exception for signatures would let the ambient `Random.Shared` back in
  through the same door.
- **Extension functions as delegates** (`Func<RdfTerm[], RdfTerm?>`). No
  interface. Rejected: a delegate cannot report an error distinctly from a
  null result without a second convention, and an interface can grow a
  member (a declared arity, a determinism flag the optimiser could use)
  additively.

## Consequences

- **A caller that uses `NOW()` writes one line**, and a server at milestone 7
  writes it once for every request.
- **Queries become reproducible under test**: the conformance harness injects
  a fixed clock and a seeded generator, and the properties compare results
  that cannot differ by time or chance.
- **Two more public interfaces** (`IExtensionFunction`, `IRandomSource`) and a
  third for aggregates (`IExtensionAggregate`, ADR 0053) enter the
  baseline, each with one or two members.
- **The optimiser must not fold or move** a call to `RAND`, `NOW`, `UUID`,
  `STRUUID` or `BNODE`, nor a call to an extension function, since it cannot
  know that one is deterministic. `sparql-evaluation.md` states the rule.

## Checks

- **Checked against the accepted ADRs** (0001–0055) and the specification.
  Touches **0011** (the ban, extended by opting in, with the list's messages
  generalised), **0003** and the brief's principle 2 (options, not a
  registry), **0053** (custom aggregates arrive the same way), and **0048**
  (the optimiser's folding rule). No conflict with any.
- **Layer ownership.** `EvaluationOptions` and the three interfaces are
  `Varve.Sparql.Evaluation`, **layer 3**.
- **Analyzer rule.** None new: `BannedApiAnalyzers` enforces the ban, as it
  does for the store, and `tests/fixtures/banned-api/` already proves the
  wiring.
- **Open questions owned.** None.
