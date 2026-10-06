# Contributing

Varve is run to the **Mind Over Machine open-source stewardship standard**
([#16](https://github.com/Hafeok/Varve/issues/16)). [`GOVERNANCE.md`](GOVERNANCE.md)
is who decides what; this is how to do the work.

## Getting set up

```bash
git clone --recurse-submodules https://github.com/Hafeok/Varve
```

Open it in the devcontainer — VS Code will offer, or `devcontainer up
--workspace-folder .` — and everything the gates need is there: the SDK pinned
to `global.json`, the WebAssembly workloads, the W3C submodule, `gh`, and
`core.autocrlf=false`. Then:

```bash
dotnet run eng/ci.cs            # the whole pipeline, exactly as CI runs it
dotnet run eng/ci.cs -- --list  # what it consists of
```

**That is the same file CI runs, in the same image** ([ADR 0036](docs/adr/0036-containerised-development.md)).
There is one definition of the pipeline and the workflow calls it, so a green
run locally means a green run in CI — for everything a linux container can
cover. The Windows, Native AOT and browser WASM legs run only in CI, and ADR
0036 says why rather than pretending otherwise.

Without the container you need .NET 10.0.401, the `wasm-tools` and
`wasm-experimental` workloads, and `git config core.autocrlf false`.

## Order of work

Specification, then ADR, then code. A decision that is not written down is not a
decision, and a rule that exists only in a document is not a rule — it is an
analyzer or it does not bind.

When a proposal contradicts an accepted ADR, say so and propose a superseding
ADR. Do not diverge quietly. When one of specification, ADR or code changes,
state in the same change what else must change.

Work in **small vertical slices ending in passing tests**, never broad
scaffolding that compiles and does nothing.

## Trunk-based development

**`main` is the trunk, and accepts only commits whose checks have already
passed** ([ADR 0088](docs/adr/0088-only-checked-commits-reach-main.md)). Land a
change in one of two ways, your choice:

- **Push it to a `land/<anything>` branch**, let CI run there, then fast-forward
  `main` to that commit: `git push origin <sha>:main`. CI on a `land/` branch
  checks everything the branch would bring to `main`.
- **Open a pull request** and merge it. It needs no review when every commit is
  yours. A pull request with an AI session's commits needs an approving review
  on its head from the session's responsible human or a delegate
  ([ADR 0087](docs/adr/0087-the-identity-map.md)).

- **Small, frequent commits.** A change not ready for the trunk lives behind a
  feature flag or stays local. **Not on a long-lived branch**: `milestone/3b`
  brought 883 conformance cases in one movement and no review of it was a
  review in any useful sense.
- **The trunk is always releasable.** That is what the gates are protecting:
  every one is a required check on the `trunk` ruleset, with no bypass.
- Human review is required for a *release*, through the `release` environment,
  and for a pull request carrying an AI session's commits. Otherwise reviews
  are welcome and **non-blocking**.
- Only the maintainer merges a pull request.

Run `dotnet run eng/ci.cs` before you push. A push that turns the trunk red
costs everyone else's next hour.

## Commits

**Conventional commits**, one logical change per commit. A commit that changes a
rule, its tests and its documentation together is one change, not three.

**The body is the argument.** What was decided, what was measured, what was
found, what is left. The diff shows what changed; the message is the only place
that can say why, and it is the only documentation guaranteed to still be there
in five years.

Every commit needs **three** things:

### 1. An issue reference

`Refs #N` in the body, or `Closes #N` if it completes the issue. Not in the
subject. Enforced by `eng/issue-refs.cs`
([ADR 0033](docs/adr/0033-commit-traceability.md)).

If no issue covers your work, open one first. That ordering is the point, not a
formality.

### 2. A DCO sign-off

```
Signed-off-by: Your Name <your@email>
```

`git commit -s` adds it. It states that you have the right to contribute the
code under this project's licence — an assertion about rights, which no
cryptography can make true for you. **Required from every commit, including
every commit made by an AI session**, because a session cannot assert anything
and the sign-off names the person who can.

The `dco` check (`eng/dco.cs`, [ADR 0087](docs/adr/0087-the-identity-map.md))
requires your sign-off to match the commit's author, name and email exactly.
An AI session's commit is signed off by the human `eng/identities.json` makes
responsible for that session, or one of their delegates.

### 3. A signature

Required on `main` for every human committer
([ADR 0034](docs/adr/0034-commit-signing-and-the-sandbox-exception.md)). A
signature is evidence of *who made the commit*; the sign-off is a statement that
they *may*. They are different things and neither substitutes for the other.

**SSH** — the least setup if you already push over SSH:

```bash
git config --global gpg.format ssh
git config --global user.signingkey ~/.ssh/id_ed25519.pub
git config --global commit.gpgsign true
```

Then add the same public key to GitHub a **second** time, under
*Settings → SSH and GPG keys → New SSH key*, with key type **Signing Key**. A
key registered only for authentication produces commits GitHub reports as
`unknown_key`.

**GPG**:

```bash
gpg --full-generate-key                     # ed25519, or RSA 4096
gpg --list-secret-keys --keyid-format=long  # take the key id
git config --global user.signingkey <KEYID>
git config --global commit.gpgsign true
gpg --armor --export <KEYID>                # paste into GitHub → New GPG key
```

Check it worked: `git log --show-signature -1`, and the commit shows **Verified**
on GitHub.

**Cloud AI sessions are the one exception**, through a ruleset bypass, and it is
a stated deviation from the standard rather than a loophole. `GOVERNANCE.md` and
ADR 0034 say what attests those commits instead.

## Traceability for AI-assisted work

The standard requires that generative AI work used in development is documented
and tied to issues ([ADR 0033](docs/adr/0033-commit-traceability.md)).

**A session leaves a record** in `docs/traceability/`, named
`YYYY-MM-DD-issue-N-<slug>.md`, containing the prompt it was given, **the tool
and the model, named exactly**, and the report it returned. The model is the
identifier the session's metadata reports; where the tool will not write it,
the record says so and the maintainer fills it in. A commit message never
carries a model's identifier (ADR 0033, amended 2026-10-02).

Prose in `README.md`, `CONTRIBUTING.md`, `GOVERNANCE.md` and the documentation
describes the project as developed with AI assistance under human review and
**names no product** — a README that advertises a vendor dates badly. The
traceability records and the issues *do* name the tool and the model, because a
record that will not say what produced a result cannot be audited.

`AGENTS.md` is the instruction file. `CLAUDE.md` points at it.

## Testing

[`docs/testing.md`](docs/testing.md) is what each kind of test is for. Beyond
it, four rules bind every change.

### Detroit-style, always

Tests exercise **real components through their public contracts**. Test doubles
only at a true process boundary — the network, the clock, a key store — and
nowhere else.

This is not a style preference, it is what makes the suite survive refactoring.
A test that asserts which methods were called on a mock is a test of today's
call graph, and it goes red on every change that improves the design while
staying green on changes that break behaviour. It also levels the ground for AI
agents and humans alike: a test that describes behaviour can be understood by
whoever reads it next, and a test that describes an interaction pattern needs
the author.

The repository already works this way and says so where it matters: "**the
memory backend is a real backend, not a test double**"
([ADR 0018](docs/adr/0018-storage-abstraction.md)), and the quad source has a
real implementation rather than a stub. **There is not one mock or fake in this
repository**, and adding the first one needs an argument.

**Property-based and conformance tests are the preferred forms.** Where a
reader and a writer are inverses, say so as a property and let the corpus supply
the cases ([ADR 0025](docs/adr/0025-property-based-testing.md)).

### The chunk-boundary oracle, for every streaming reader

Parse whole, parse again split at every byte offset, require the same quads or
the same error kind *and position*. A new reader is not done until its suites
are in `ConformanceSuite.OracleCorpus`, and
`Every_format_is_covered_by_the_chunk_boundary_oracle` fails when a format is
added without one.

This holds for a line-based reader that does not need the mechanism as much as
for one that does: **the argument is not the measurement**. It has found nine
defect classes, two of which produced *wrong quads rather than errors*.

### Test data is version-controlled and never fetched at test time

The W3C suites come from a git submodule **pinned by commit**; generated
fixtures are checked in. **Nothing downloads anything during a test run.** A
suite that fetches its inputs is a suite whose result depends on a web server's
mood, that cannot run offline or in a sealed build, and that silently changes
meaning when the upstream does.

`SubmoduleGuardTests` pins the case count per suite. A manifest that stops being
read makes its suite pass by having nothing in it, and "more than zero" does not
catch a suite read as nine cases instead of three hundred and thirteen.

### Prove a gate's failure path by running it

A gate that has never failed is a gate nobody has tested. `eng/native-assets.cs`
is pointed at a real restore graph containing a native asset and at one that
does not, and both outcomes are asserted; `eng/licence-headers.cs` has a fixture
holding one conforming and one violating file.

## Conformance

The W3C test suites are the acceptance gate. A feature is not done until its
manifest entries pass or each failure carries a written, justified exemption.

`tests/Varve.Conformance.Tests/baseline/passing.txt` lists what passes today,
sorted by test IRI. `eng/ratchet.cs` fails the build when one of them stops
passing, and prints tests that newly pass. **When your change makes tests pass,
update the baseline in the same commit.**

`baseline/exemptions.txt` is empty and that is a result, not a default. An
exempt case is neither required to pass nor reported as newly passing, and **an
exemption with no written justification fails the run** — the cost of an
exemption is writing down why.

An exemption is also a versioning artefact
([ADR 0035](docs/adr/0035-semantic-versioning.md)): adding one to make a suite
green is accepting a behaviour change, and it deserves the scrutiny of deleting
a line from a public API baseline.

## Adding an ADR

Take the next free number — `docs/adr/` is dense and numbers are never reused —
and write five sections: **Status**, **Context**, **Decision**, **Alternatives
considered**, **Consequences**. Add a row to `docs/adr/README.md`.

**Every ADR is also a decision set** in `docs/decisions/`
([ADR 0062](docs/adr/0062-adopting-decisiondriven-analyzers.md)): one file,
named for the ADR's title, listing each ruling as a key with a one-line
statement. A ruling the new ADR supersedes moves into its set under the same key.
[`docs/decisions/README.md`](docs/decisions/README.md) has the format, and
`eng/decision-sets.cs` checks it.

*Alternatives considered* is not a formality. An ADR that lists no losing option
has not recorded a decision, only an outcome, and the next person cannot tell
whether the alternative was rejected or never seen.

**An accepted ADR is never edited.** A decision that still stands but needs more
detail gets a **dated amendment** inside it (add-only and dated, [ADR 0068](docs/adr/0068-dated-amendments.md)); a decision that changed gets a
**superseding ADR**, and the old one keeps its text so the reasoning that was
wrong stays readable.

Where an ADR is accepted ahead of the evidence, it states a **revisit
condition**: a fact which, if it turns out to be true, supersedes it. ADR 0020's
condition fired on a real browser build, which is what the mechanism is for.

Anyone may propose one — there is an issue template.

## Adding a rule

A rule that knows nothing about Varve belongs in `DecisionDriven.Analyzers`, as
an issue or a pull request there (ADR 0062); Varve configures it and does not
copy it. A rule that needs a Varve fact (a layer, the hot-path list) is a
`VARVE` rule, delivered as three things, in this order of importance:

1. the analyzer, in `src/Varve.Analyzers/`,
2. its tests, with at least one violating and one conforming sample,
3. the page in `docs/rules/VARVE000n.md`, linking to the ADR that motivates it.

A rule without an analyzer is a suggestion. Ids are allocated in
[ADR 0004](docs/adr/0004-enforcement-by-analyzers.md); take the next free one
and do not reuse a retired id.

## When a `DD` or `VARVE` rule fires

Every project in `src/` builds under `DD0001`–`DD0019` and
`VARVE0003`–`VARVE0005` at error severity. Read the rule's page first (the
analyzer repository's `docs/rules/` for `DD`, `docs/rules/` here for `VARVE`);
its message ends in `Decide:`, with two paths.

1. **The design change.** If the code looks like this only because it always
   did, change it: a wrapper type for a naked primitive (ADR 0065), a
   `[Contract]` citing the decision that made an abstraction, a reused buffer
   for an allocation on a hot path.
2. **A decision.** If the code is right and the rule's reason does not apply,
   put `[DesignDecision(typeof(<Set>.<Key>), Scope = ...)]` on the narrowest
   symbol that carries the exception, citing a decision in `docs/decisions/`.
   If none says so, file one: add the key to the set its ADR owns, with a
   one-line statement and no `accepted-by`. The citation is `CS0618` until the
   maintainer accepts it; the pull request says so and is red on that alone
   ([ADR 0066](docs/adr/0066-expected-red-pull-requests.md)).

**A hot path** is a member marked `[HotPath]`, or a member of a marked type,
or an override or implementation of a marked member. Mark what runs per quad,
per row or per term (a scan, an operator's `Open` and its iterator, a decode
loop), not the orchestration that runs once per commit or per query. Then
`VARVE0003` holds it to no allocation and no call into unmarked code. A BCL
member it calls is admitted by the allow-list in `.editorconfig`, by type or by
member, and adding to the list is a change to a decided list
(`HotPathScope`). Run the allocation benchmark before and after a change to a
hot path, and put both numbers in the pull request.

**A false positive is an upstream issue**, with a reproduction, filed in the
analyzer repository, which never names Varve. Leave the finding unfixed if the
issue blocks it, and say so in the pull request. Never work around it here.

## Adding a dependency

Every third-party package needs an ADR, build-time and test-time included
(constraint 4). The ADR states what the package does, why the BCL does not
suffice, and whether it ships as a runtime dependency. Anything shipping a
native asset is out under constraint 1, with no exception process.

Versions are central, in `Directory.Packages.props`, each with an `Adr`
attribute. Project files carry no `Version`. **Do not take a version from
memory** — resolve the current stable version when you add it.

**Automated updates are classified** by ADR 0009's amendment of 2026-09-22:

| Change | What it needs |
|---|---|
| Patch or minor bump of an already-cited package | nothing; it passes |
| **Major** bump | the cited ADR must change in the same diff |
| **New** package | the cited ADR must change in the same diff |

`dotnet run eng/dependency-register.cs -- --base origin/main` is what classifies
it. A major bump citing an untouched ADR claims the decision that admitted 4.x
covers 5.x with nobody having looked.

## Adding a syntax or a suite

1. Wire the manifest into the conformance harness.
2. Add the suite to `ConformanceSuite.OracleCorpus` — the guard fails if you do
   not.
3. Pin the case count in `SubmoduleGuardTests`.
4. Add the passing cases to `baseline/passing.txt`; justify every exemption.

Wiring a suite whose feature is mostly unimplemented files a hundred exemptions,
which is not gating a feature — it is recording that it is absent in a file
nobody reads twice. `docs/spec/turtle.md` §9 is what to do instead: reject the
constructs outright, and prove the rejection with tests.

## Adding a package

New projects declare `<ArchLayer>` — 0–6; a test or analyzer assembly declares
none — enforced by `DD0001` and `VARVE0005` (ADR 0064). An executable is a host
and declares 6, and only an executable may (ADR 0060); a host also sets
`<ArchCompositionRoot>true</ArchCompositionRoot>`. References go
strictly downward; **same-layer references are violations**, and there is never
a `Common`, `Core`, `Utils`, `Helpers` or `Abstractions` package.

A packable project sets `IsPackable`, keeps `PublicAPI.Shipped.txt` and
`PublicAPI.Unshipped.txt` beside it, and carries its own `README.md`.

## Suppressions

**A `DD` or `VARVE` rule is never suppressed**: no `#pragma`, no
`[SuppressMessage]`, no `.editorconfig` downgrade. `DD0008` reports each. The
only exception path is `[DesignDecision]` on the symbol, citing a filed
decision in `docs/decisions/` (ADR 0062):

```csharp
[DesignDecision(typeof(StaticState.PoolsExemptByDesignDecision), Scope = ExceptionScope.Pool)]
```

For every other rule (IL-prefixed, CA, RS, BannedApi) a suppression carries a
justification that cites an ADR number, at the narrowest scope, and that is a
review obligation (ADR 0004). **A repository-wide `NoWarn` for a `VARVE` or
IL-prefixed rule is not an allowed form.**

## Licence

Varve is **MPL-2.0** ([ADR 0031](docs/adr/0031-licence-mpl-2-0.md)), which is
file-level copyleft. **Every `.cs` file begins with the notice**:

```csharp
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
```

`eng/licence-headers.cs` fails the build without it, and `.editorconfig` carries
`file_header_template` so your IDE inserts it. Under a file-level licence the
header is the mechanism, not decoration: §1.4 defines Covered Software as the
form carrying that notice.

**No code is copied from Oxigraph, dotNetRDF or anywhere else.** Read other
implementations for behaviour; write our own. This is unaffected by what their
licences permit.

## Style

`.editorconfig` is enforced at build time and warnings are errors. Prose in
documentation is plain and precise, with no marketing tone; diagrams as Mermaid
or SVG when structure is easier to see than to read.

Disagree with reasons. Cite specification sections, and say so rather than
guess.
