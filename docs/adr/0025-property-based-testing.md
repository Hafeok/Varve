# 0025 — CsCheck for property-based testing

## Status

**Accepted.** 2026-09-21.

## Context

`docs/brief.md`: property-based tests are expected for parser and serialiser
round trips, dictionary encoding, index ordering, projection rebuild
equivalence and as-of read consistency. The specification's §10 turns that into
a named list of invariants with a test each.

Milestone 3a needs the first of them. The W3C syntax suites prove we accept and
reject the right documents and prove nothing about the quads produced, because
they contain no evaluation tests — so a round-trip property is the only thing
standing between a parser that reads a document and a parser that reads it
*correctly*.

Constraint 4 applies: a test-time package still needs an ADR.

## Decision

**CsCheck 4.9.1**, Apache-2.0.

Verified on 2026-09-21 by inspecting the package:

| | CsCheck 4.9.1 | FsCheck 3.4.0 |
|---|---|---|
| Licence | Apache-2.0 | BSD-3-Clause |
| Target frameworks | net8.0 | netstandard2.0 |
| **Dependencies** | **none** | `FSharp.Core` |
| Native assets | none | none |
| xUnit binding | not needed — generators are plain values | `FsCheck.Xunit.v3`, which adds `FSharp.Core` and `xunit.v3.extensibility.core` |

**Zero dependencies decides it.** Constraint 4 says prefer the BCL and justify
every package; a package that brings nothing else with it is the cheapest
possible "yes". FsCheck is a good library and its cost is `FSharp.Core` —
a multi-megabyte runtime in the test graph for a repository with no F# in it,
plus a second package for the xUnit binding.

CsCheck is also plain C# with no DSL to learn: a generator is a value and a
property is a lambda, which matters when the person reading the test is trying
to decide whether the property is the right one.

## Alternatives considered

- **FsCheck**, the reference implementation of QuickCheck for .NET, with the
  better-known shrinking story. Lost on `FSharp.Core` alone. Had it been
  dependency-free the decision would have gone the other way on maturity.
- **Hand-written generators with xUnit `[Theory]` data.** No package at all,
  which constraint 4 prefers by default. Lost on what it silently omits:
  shrinking. A failing round-trip on a randomly generated literal with three
  escapes and a surrogate pair is nearly useless without a shrinker to reduce
  it to the one character that broke, and writing a shrinker is writing the
  library.
- **Deferring property tests to milestone 4**, when the store's invariants make
  them unavoidable. Lost because 3a is exactly where they are most needed: the
  suites test syntax acceptance, and nothing else would test that a parsed quad
  is the quad that was written.

## Consequences

**One more test-time package, with no tail.** It never reaches a published
artifact (ADR 0009's test-only class), and it is one row in the register.

**Generators are ours to write, and they are the test.** A round-trip property
is only as good as the terms it generates — the ones that matter are escapes,
surrogate pairs, language tags with a base direction, IRIs with `ucschar`, and
nested triple terms. A generator that quietly never produces a surrogate pair
makes the property vacuous, so the generators need review as carefully as the
properties.

**CsCheck targets net8.0**, which a net10.0 test project consumes without
comment. If it ever stops being maintained the replacement cost is the
generators, not the properties.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0020–0024).
  Touches **0009** (test-only class; a register row citing this ADR) and
  **0007** (the property tests are what the conformance suites cannot cover,
  which is why both exist). No conflict with any.
- **Layer ownership.** None — a test-time package, referenced by test projects
  only, which declare `VarveLayer=none`.
- **Analyzer rule.** None.
- **Open questions owned.** None.
