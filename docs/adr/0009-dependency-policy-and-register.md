# 0009 — Dependency policy and the enforced register

## Status

Accepted. 2026-09-21. Supersedes [0006](0006-build-and-test-dependencies.md).

## Context

`docs/brief.md`, constraint 4: minimal dependencies, prefer the BCL, every
third-party package needs an ADR — build-time and test-time ones included.

[ADR 0006](0006-build-and-test-dependencies.md) discharged that by enumerating
every package in a table. It was amended twice inside the milestone that
accepted it: once for a package it had missed, once for a transitive pin, and a
third time when the version policy it stated turned out to be wrong for one
package. **An ADR that enumerates will be amended forever**, and each amendment
makes it less readable as a decision and more like a stale copy of
`Directory.Packages.props`. Two artefacts holding the same list is one artefact
too many; the one that the build actually reads should win.

There is a second problem, and it is the one that matters. ADR 0004 says a rule
that exists only in a document is not a rule. Constraint 4 was exactly that: a
document said every package needs an ADR, and nothing checked it. A package
could be added with no ADR, or citing one that does not exist, and the build
would be perfectly green.

## Decision

The ADR states the **policy**. `Directory.Packages.props` is the **register**.
A script in `eng/` makes the register a gate.

### Policy

**Prefer the BCL.** A package enters only when the BCL does not do the job, and
the register says which decision admitted it.

**No native assets in anything that ships.** A package that ships a native
binary is refused under constraint 1.

> **Amended 2026-09-21** (see [ADR 0027](0027-benchmarking.md)). This originally
> read "no exceptions … a property of the package, not of how we intend to use
> it, and there is no exception process". That was my wording, not the brief's,
> and it was wrong at the edge: constraint 1 is "100% managed code", which
> describes **Varve** — the thing a consumer installs, trims, compiles ahead of
> time and runs in a browser. It does not describe a benchmark harness.
>
> The refusal therefore binds the **runtime class**, and anything that can
> reach a published Varve package. It does not bind build-time or test-only
> packages that never leave the machine. The test is "can this reach a
> published artifact", and it is answered by whether the referencing project is
> packable.
>
> What forced the correction: the brief requires BenchmarkDotNet numbers, and
> **BenchmarkDotNet 0.15.8 depends transitively on `Gee.External.Capstone`
> 2.3.0**, which ships nine native libraries —
> `runtimes/{linux-arm,linux-arm64,linux-x64,linux-x86}/native/libcapstone.so`,
> two `.dylib` for macOS and three `capstone.dll` for Windows. That is the one
> package that forced this amendment, and the absolute rule forbade something
> the brief mandates.
>
> **Confirmed by the repository owner on 2026-09-22.** The substance was right,
> but narrowing a hard constraint from `docs/brief.md` was not mine to do
> alone, and this records that it was ratified rather than assumed.

> **Enforced from 2026-09-22.** `eng/native-assets.cs` runs in the build job and
> fails when any package in the restore closure of a **packable solution
> member** contributes a native runtime asset. A constraint that is narrowed and
> not then enforced in its narrowed form is a sentence, and the narrowing is
> exactly what makes an unenforced version plausible: the original was
> absolute and obvious, the scoped one has an edge somebody has to check.
>
> The signal is `assetType: "native"` in `project.assets.json`, not a path
> containing `/native/`. `System.Diagnostics.EventLog` ships
> `runtimes/win/lib/…`, which is managed, and TraceEvent ships
> `build/native/…`, which is an MSBuild-time file nobody deploys; a path match
> would report both, and a gate that cries wolf is a gate that gets a `NoWarn`.
>
> Both outcomes are proven against **real restore graphs** rather than a
> fixture: the benchmark harness's, which must fail and name Capstone's nine
> files, and `Varve.Turtle`'s, which must pass. A hand-written assets file
> would test the expectation rather than the gate. A missing assets file exits
> 2 — could not run — and not 0, because a gate that cannot read its input has
> not found the code clean.

**Three classes, admitted on different terms:**

| Class | What it may be | Bar |
|---|---|---|
| **Runtime** | A dependency of a published Varve package | Highest. A consumer pays for it forever, in binary size, trim surface and their own audit. Needs an ADR that argues the case specifically. The published dependency list is empty unless an ADR says otherwise. |
| **Build-time** | Analyzers, source generators, MSBuild tooling | Lower. Never reaches a consumer's dependency graph. Constraint 2 does not apply to it: it runs in the compiler, is never published, and its allocations are irrelevant. |
| **Test-only** | Test frameworks, harnesses, fixtures | Lower still, and **may be temporary**. A test-only package admitted as a stopgap carries a stated exit criterion, and remaining past it is a defect rather than a fact. |

**Versions are central and resolved, never remembered.** Every version lives in
`Directory.Packages.props`; no project file carries a `Version` attribute. A
version is resolved from nuget.org when the package is added or changed. The
default is the latest stable release.

**The latest-version default has one named exception: a floor.** Where a package
determines what *host* can load our output, the correct value is the oldest
version that supports the API we use, not the newest that exists. Today that is
exactly one package.

> **The Roslyn floor.** `Microsoft.CodeAnalysis.CSharp` is pinned to **5.0.0**,
> the Roslyn line shipped with the first .NET 10 SDK (10.0.100). An analyzer
> requires a host at or above the Roslyn it was built against, and an IDE's
> compiler comes from the IDE rather than from `global.json` — so building
> against the latest makes the analyzer silently fail to load for anyone on an
> older Visual Studio, and a rule that does not load reports nothing, which
> reads exactly like a rule that found nothing.
> `Microsoft.CodeAnalysis.CSharp.Workspaces` follows the same floor so the test
> harness cannot pass against an API the shipped analyzer would not have.
> `Microsoft.CodeAnalysis.Analyzers` and the testing package do **not**: they
> run in our build, never in a consumer's host.
>
> Note what this does *not* resolve. `global.json` pins the SDK to 10.0.401, so
> a contributor on 10.0.1xx cannot build this repository at all, and the floor
> protects IDE load rather than the command line. That mismatch is deliberate:
> the SDK pin keeps CI and local builds on one known compiler, and the floor
> keeps the analyzer loadable in editors we do not pin. Changing either is a
> superseding ADR.

**A floor is raised only with a reason.** "It is old" is not one.

### The register

Every `PackageVersion` in `Directory.Packages.props` carries `Adr="NNNN"`,
naming the decision that admits it — transitive pins included, since a pin is a
dependency decision like any other.

Most cite this ADR, which is the right answer when the package is admitted by
the policy alone (a test framework, the compiler API). A package admitted by a
*specific* decision cites that decision instead: `dotNetRdf.Core` and the TRX
reporter cite [0007](0007-w3c-conformance-harness.md), which decides the
conformance harness and owns the exit criterion; the public-API and
banned-symbol analyzers cite [0004](0004-enforcement-by-analyzers.md), which
decides off-the-shelf enforcement. A citation that points at the reasoning
rather than at the policy is worth more, so prefer the specific one.

### The gate

`eng/dependency-register.cs` — a C# file-based app, run with `dotnet run` — fails
when a `PackageVersion`:

- carries no `Adr` metadata, or
- cites a number with no matching `docs/adr/NNNN-*.md`.

It runs in the **`build`** job, before restore, on both operating systems. It is
a text check over two inputs and should fail in seconds rather than after a
compile.

### Automated dependency updates

> **Amended 2026-09-22** (see [ADR 0033](0033-commit-traceability.md) and
> [#16](https://github.com/Hafeok/Varve/issues/16), the adoption of the Mind
> Over Machine stewardship standard). Dependabot now opens pull requests for
> GitHub Actions and NuGet, and the policy as written above would fail every
> one of them: each bump edits `Directory.Packages.props`, and nothing in the
> original decision distinguished a version bump from a new dependency.
>
> A gate that fails every automated update is a gate people route around, and
> the register would become a thing to be satisfied rather than a thing to be
> read. So the policy is refined rather than relaxed, at the point where a
> version change can mean something the ADR that admitted the package did not
> consider:
>
> | Change | Needs |
> |---|---|
> | Patch or minor bump of a package that already cites an ADR | nothing; it passes |
> | **Major** bump | an ADR entry |
> | **New** package | an ADR entry |
>
> **Everything the original decision said still holds.** Every package still
> names the decision that admits it, a dangling citation still fails, and a
> package shipping a native asset is still refused under constraint 1 by
> `eng/native-assets.cs`. What is added is that maintenance no longer needs a
> decision, because maintenance is not one.
>
> "Needs an ADR entry" is made checkable rather than left to judgement: the
> cited ADR must itself have changed in the same diff — a new ADR, or a dated
> amendment like this one. A major bump that cites an untouched ADR is a claim
> that the decision admitting 4.x covers 5.x with nobody having looked, and
> that claim is worth interrupting. A semantic version is a promise about
> compatibility ([ADR 0035](0035-semantic-versioning.md)), so a major bump is
> the upstream telling us the promise is broken; taking them at their word is
> the cheapest correct policy.
>
> `eng/dependency-register.cs --base <ref>` implements it. Without `--base` the
> gate behaves exactly as before, which is why nothing about the plain CI run
> changes. A version the gate cannot parse is classified as major: a register
> entry whose version is a range or an expression is not something to wave
> through.

## Alternatives considered

- **Keep 0006's shape and amend it.** Honest, and it is what ADR 0001's
  supersession rule contemplates. Rejected because the amendments are not
  decisions — they are bookkeeping — and a decision record filling with
  bookkeeping stops being read. The deeper objection is that it leaves
  constraint 4 unenforced however carefully the table is maintained.
- **One ADR per package.** Maximum traceability; every package's citation points
  at prose written for it. Rejected as ceremony: ten packages would become ten
  ADRs that mostly say "a test framework, and the BCL has no assertion model".
  The register plus a class in the policy carries the same information where it
  can be read at a glance.
- **A comment convention in `Directory.Packages.props`.** Zero machinery. This
  is roughly what 0006 already left behind, and it is exactly the thing ADR 0004
  rejects: a comment can say anything, including the number of an ADR that was
  never written.
- **Enforce with an MSBuild target instead of a script.** It would fail inside
  the build rather than beside it, which is arguably better placement. Rejected
  for now: MSBuild makes a readable multi-line error hard, and makes the
  failure paths hard to exercise. The script takes a path argument precisely so
  that both of its failures are run before it is trusted, the way
  `eng/ratchet.cs` was. Worth revisiting if `eng/` grows a third script.
- **Also reject a citation whose ADR is superseded.** Tempting, and arguably the
  stricter reading of "the decision that admits it". Not built: a superseded ADR
  still exists, and a citation of one is a signal to read rather than an error
  to fail on — the supersession chain is short and ADR 0001 already requires the
  superseding ADR to name what it replaced. Reconsider if a citation is ever
  found pointing at a decision that was reversed.
- **Gate on the resolved dependency graph** rather than on declared versions,
  which would also catch a transitive package nobody declared. Complementary,
  not a substitute, and the same shape as the package-graph check ADR 0003
  wants. Not built at this milestone.

## Consequences

Adding a package now means two things, not one: a version in the register and a
number beside it. If no ADR justifies it, the honest outcome is that one has to
be written — which is constraint 4 stated as a workflow rather than as a wish.

ADR 0006 stays in the directory, superseded. Its package table is now a
historical snapshot and will go stale; the register is the live list. The
amendment on its version-choice paragraph is kept deliberately, because the
mistake it records — a green check that could not have failed — is the reason
this ADR distinguishes a floor from a latest.

The gate is a text check and knows nothing about what a package does. It cannot
tell a well-argued ADR from a stub. It catches the omission and the dangling
citation, which are the failures that actually happen; judging whether the
argument is good remains review's job.
