# 0007 — W3C conformance harness

## Status

Accepted. 2026-09-20.

## Context

`docs/brief.md`, definition of correct: the W3C test suites are the acceptance
gate, they run in CI from the first parser onward, and a feature is not done
until its manifest entries pass or each failure has a written, justified
exemption.

"From the first parser onward" is the part that shapes this decision. If the
harness arrives with the first parser, its first run is also the run in which
the parser is judged, and there is no way to tell a harness bug from a parser
bug. Building the harness while there is provably nothing to pass means its
first run has a known-correct expected result: everything fails, for one stated
reason.

## Decision

### Test data

`w3c/rdf-tests` as a git submodule at `tests/w3c/rdf-tests`, pinned to commit
`369a90d1a60c021b746df2e411da0ff36258a758` (`main`, as of 2026-09-20).

A submodule rather than vendored files: the data is not ours, its provenance
should stay visible, and a pinned commit makes "which suite version did this
pass against" answerable from the repository alone. Advancing the pin is a
commit that says so, and any resulting change in pass counts shows up in the
ratchet rather than in a diff nobody reads.

`.gitattributes` excludes the submodule from line-ending normalisation. The
byte content of the test data is the thing under test; a parser must meet CRLF
as data, not as an artefact of how the repository was cloned.

### Suites

Milestone 1 wires two: `rdf/rdf11/rdf-n-triples` and `rdf/rdf11/rdf-n-quads`.
Each manifest entry becomes one test case, named by its test IRI.

Discovery is a table — suite id, manifest path, format — so adding a suite is
one line. The ordering and naming are stable across runs so that the ratchet's
baseline is a stable set of strings.

### Reading the manifests

The manifests are Turtle. We have no Turtle parser, which is the circularity
this decision has to break.

A **test-only** dependency on `dotNetRdf.Core`, confined to **one internal
class** in `Varve.Conformance.Tests`, does the manifest reading. Nothing else in
the test project touches it, and nothing packable references it. See ADR 0006
for the dependency justification.

**Exit criterion, and it is a criterion and not an aspiration:** the dependency
is removed when `Varve.Turtle` passes the `rdf/rdf11/rdf-turtle` suite
unexempted. At that point manifest reading switches to `Varve.Turtle` and
`dotNetRdf.Core` leaves `Directory.Packages.props`. Due at milestone 5. If it is
still present after milestone 5, that is a defect.

> **Met at milestone 3b, two milestones early.** `Varve.Turtle` passes
> `rdf/rdf11/rdf-turtle` 313 of 313 and `rdf/rdf11/rdf-trig` 357 of 357, both
> unexempted, so manifest reading moved to it and the package reference left
> `Varve.Conformance.Tests`.
>
> Two things kept the switch honest. The per-suite case counts were **recorded
> while dotNetRDF was still reading the manifests** — 70, 87, 29, 27, 313, 357
> — so the guard against the new reader was written by the one it replaced; a
> parser bug that dropped entries changes a count. And
> `The_harness_does_not_reference_another_rdf_implementation` makes the
> criterion a test rather than this paragraph.
>
> `dotNetRdf.Core` stays in `Directory.Packages.props` for the **benchmark**
> project alone, on ADR 0027's separate justification: a performance claim needs
> something to compare against. That is a different assembly from the harness,
> and this criterion was about the harness.

Using our own parser to read the manifests that judge our own parser is a real
circularity, and it is worth being clear about why it is acceptable at that
point and not now. A Turtle parser that passes its own W3C suite has been judged
by data it did not read with itself — the suite's *data* files are Turtle, but
the pass/fail verdict for each case comes from the manifest, and a manifest
misread would show up as a test that vanishes or a suite whose count changes.
The ratchet is what makes that visible: a silent drop in enumerated cases is
exactly what it exists to catch.

### The subject under test

The harness defines its own abstraction on the test side: given a file and a
format, parse it, and report success or failure — and, once there is something
to compare, the quads. It is deliberately *not* a design for the parser API. The
adapter is test code and will wrap whatever the real API becomes.

**No subject is registered.** Every case fails with "no parser registered". That
is the correct result for milestone 1 and it is the harness's own first test: a
run that reports anything else — zero cases, a different message, an error
before the cases are reached — is a harness defect.

One separate guard test fails if the submodule directory is missing. Without it,
an un-checked-out submodule enumerates zero cases and the suite passes silently,
which is the one failure mode that would make the gate worthless.

### The ratchet

`tests/Varve.Conformance.Tests/baseline/passing.txt` holds the test IRIs that
pass, one per line, sorted. It starts empty.

`eng/ratchet.cs` — a C# file-based app, run with `dotnet run` — reads the TRX
produced by the conformance run and:

- **fails** when an IRI in the baseline is no longer passing, naming each one;
- **prints** IRIs that pass but are not in the baseline, with the lines to add,
  so the baseline moves in the same pull request as the change that earned it;
- **fails** when the baseline names an IRI the run did not contain at all, which
  catches a renamed or silently dropped case.

Newly passing tests do not fail the run. A ratchet that failed on improvement
would be a ratchet people route around.

CI runs the conformance tests with `continue-on-error` and gates on the ratchet.
The raw test step is expected to be red for a long time; its exit code carries no
information until every suite passes, and the ratchet is what carries it
meanwhile.

`eng/` scripts are C# file-based apps rather than a bash and PowerShell pair, so
there is one implementation to keep correct and it runs identically on ubuntu and
windows.

## Alternatives considered

- **Vendoring the W3C files.** Simpler clone, no submodule friction. Rejected:
  provenance disappears, updates become large opaque diffs, and the licence and
  attribution of third-party data get muddled with ours.
- **Fetching the suites in CI** rather than pinning. Rejected: the gate would
  change under us without a commit, and a red build could be caused by an
  upstream edit with nothing in our history to point at.
- **Pre-converting the manifests to N-Triples** with an external tool, checked
  in. Removes `dotNetRdf.Core` entirely. Rejected: it puts a non-.NET tool in CI,
  and the checked-in conversion can drift from the submodule with nothing to
  detect it.
- **A hand-written Turtle subset parser for manifests.** Rejected in ADR 0006:
  a parser written to read the files that judge our parser is a conflict of
  interest, and its bugs would be debugged as parser bugs.
- **One test case per suite** rather than per manifest entry. Far cheaper to
  write. Rejected: it loses the per-entry granularity the ratchet needs, and the
  brief requires per-entry exemptions to be written and justified, which means
  per-entry identity.
- **Expected-failure annotations in the test code** instead of a baseline file.
  Rejected: it puts the conformance state in code that must be recompiled, and
  makes "what changed" a code review rather than a one-line data diff.

## Consequences

A clone without `--recurse-submodules` cannot run the conformance suite. The
guard test makes that a clear failure rather than a silent pass, and
`README.md` and `CONTRIBUTING.md` say so.

The conformance job is red from this commit until the first parser lands. That
is intended and is why it does not gate; the ratchet gates instead. The risk is
that a permanently red job gets ignored — the per-suite pass and fail counts
written to the step summary exist so that the number, rather than the colour, is
what people read.

Advancing the submodule pin may change which cases exist. The ratchet's third
check — a baseline entry the run did not contain — is what turns that from a
silent loss of coverage into a build failure.
