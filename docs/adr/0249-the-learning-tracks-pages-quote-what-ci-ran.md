# 0249 — The learning track's pages quote what CI ran

## Status

**Accepted — filed unaccepted by #364, 2026-11-19** (ADR 0066), under the
learning track, #94, and accepted by the maintainer on 2026-11-24, written by his
delegate (ADR 0239). It records the plan the maintainer accepted on #94 on
2026-11-19, answers 1 to 5 included. Numbered 0249, after
[0248](0248-every-procedure-names-what-runs-it.md), whose marker grammar this
ADR extends.

Records decision set
[`the-learning-tracks-pages-quote-what-ci-ran`](../decisions/the-learning-tracks-pages-quote-what-ci-ran.md).

## Context

#94 is a learning track for SQL developers: eleven chapters in `docs/learn/`,
each with a runnable sample in `samples/learn/`, on one RealEstateCore
portfolio (ADR 0141). Its session prompt of 2026-11-19 sets the rules:

- every page quotes its sample's real output, and CI asserts it;
- no REC term is used before the chapter that introduces it;
- a chapter shows only what the release it is written against does, and
  anything that hasn't shipped is a marker naming its issue;
- the samples are built the way a user builds: small projects that
  reference the `Varve.*` packages and nothing internal, trimmed with no
  warning, with no `System.Uri` where `Varve.Iri` is the type;
- every sample's data validates against the pinned REC 4 shapes;
- the pages are on the docs site, under its link gate (ADR 0247).

A rule only in a document is not a rule. A page's quoted output is drift
waiting to happen: the first change to the evaluator's ordering, a sample or
the data leaves the page showing what nobody ran. The same holds for a term
used early, and for a "not yet" left on a page after the feature ships.

[ADR 0248](0248-every-procedure-names-what-runs-it.md) gives the operator guide a marker on the line before every
fenced block, naming the job that runs it, `none` with a reason, or
`example`, and a gate, `eng/procedures.cs`, that checks the markers name
real jobs. It doesn't check that a block equals what the job produced,
which is what this track needs.

## Decision

1. **Every fenced block in `docs/learn/*.md` carries ADR 0248's marker**,
   in its grammar: `<!-- ci: <jobs> — <note> -->`, `<!-- ci: none — <reason>
   -->` or `<!-- ci: example — <what> -->`. A page with a `none` has a
   section "Not run in CI". `eng/procedures.cs` keeps its scope, the
   operator guide. `eng/learn.cs` holds `docs/learn/` to the same grammar,
   and to points 2 to 5.

2. **Two notes quote a file, and the block must equal it.**
   - `output <path>`: the block is the whole file, a section of a sample's
     output kept in `samples/learn/<NN-name>/expected/<section>.txt`.
   - `source <path>#<region>`: the block is the region of that file between
     `// region: <name>` and `// endregion: <name>` (`#` for TriG), dedented.

   Equal means byte for byte after line endings and trailing whitespace.
   Pages are checked, not generated: a page reads correctly on GitHub as it
   stands. `eng/learn.cs -- --run --write` writes the expected files and then
   the quoted blocks, by hand and never in CI.

3. **Every sample runs in CI, and prints what `expected/` holds.**
   - A sample prints named sections (`== name ==`) and, with `--state
     <file>`, writes its dataset's head as canonical N-Quads (RDFC-1.0).
   - `eng/learn.cs -- --run` builds each `samples/learn/NN-*` against `src/`,
     runs it, and fails on a section that differs from its expected file, a
     section missing either way, or a final state other than
     `expected/state.nq`.
   - `--trimmed <rid>` publishes each sample trimmed for that runtime, with
     every warning an error, and runs the published executable instead.

4. **No REC term before its chapter.** `docs/learn/terms.md` is a table of
   chapters and the `rec:` and `brick:` terms each introduces. `eng/learn.cs`
   reads every term, prefixed or as a full IRI, in every page (the appendix
   counts after every chapter) and in every file of a sample and of
   `samples/learn/data/NN-*`. It fails on a term used before its chapter, or
   in no row.

5. **What has not shipped is a marker that names its issue.**
   `<!-- unshipped: #N -->`, followed by a quote that starts "**Not in this
   release.**" and names #N. The gate fails when #N is in the `basis:` of a
   descriptor under `releases/`: the feature has shipped, and the page still
   says it hasn't.

6. **A sample references Varve as a user does.**
   - A sample's project names the packages it uses as `VarveReference`
     items, and nothing else: no `ProjectReference`, `PackageReference`,
     `Reference`, `Compile` or `Import`.
   - Each `VarveReference` names a packable project under `src/`.
   - Nothing in `src/` names a sample in `InternalsVisibleTo`.
   - `samples/learn/Directory.Build.targets` resolves a `VarveReference`
     to the project under `src/` by default (`VarveFrom=project`), so CI
     builds the samples against `main`.
   - With `VarveFrom=package` and `VarvePackageVersion`, it resolves to the
     published package. That build restores unlocked: it is a check, never
     a ship.
   - The nightly builds every sample against the newest published preview
     that way.

7. **The samples hold the constraints.**
   - They build with the trim, AOT and single-file analyzers and warnings as
     errors.
   - They publish trimmed (`PublishTrimmed`, set in the samples' props so it
     does not flow into `src/`).
   - They read `eng/BannedSymbols.Uri.txt`: no `System.Uri` (ADR 0004).
   - They are layer-6 composition roots (ADR 0060) under every `DD` and
     `VARVE` rule.

8. **Every sample's data conforms to the pinned REC 4 shapes.**
   `tests/Varve.Learn.Tests` validates the slice, chapter by chapter, and
   every `expected/state.nq` against REC 4 composed as the REC gate composes
   it (ADR 0176). `samples/learn/rec.pin` names the tag and the SHA-256 of
   its `SHA256SUMS`, and the test fails, before reading a byte, when the tag
   or a file moves.

9. **The jobs.**
   - `learn`, `learn-fixtures` and `test-learn` in `eng/ci.cs`.
   - ci.yml's `learn` on Linux, Windows and macOS.
   - `learn (trimmed)` on Linux.
   - The nightly's trimmed runs on Windows and macOS, and its build against
     the published packages.

   `eng/learn.cs`'s failure paths are proven on `tests/fixtures/learn/`.

## Alternatives considered

- **Generate the pages, or include files at build time.** Rejected. The
  site has no include mechanism (ADR 0247), and a page that is a template
  doesn't read correctly on GitHub. Checking the page as written gets the
  same guarantee.
- **Extend `eng/procedures.cs` to `docs/learn/`.** Rejected for now. Its
  scope is ADR 0248's, and its markers name jobs without comparing what
  they print. The grammar is shared, so a later merge of the two gates
  changes no page.
- **Samples as `PackageReference`s on every pull request.** Rejected. A pull
  request could not change a sample and the package it teaches together,
  and the samples would lag `main`. The nightly's package build keeps the
  user's path honest.
- **Run the REC generator in the samples.** Rejected. The generator is
  internal to a test project (ADR 0141), and a sample references nothing
  internal. From chapter 3, the generated portfolio is a fixture the
  generator's tests write and pin.

## Consequences

- A change to the evaluator, the store or the data that changes what a
  sample prints fails `learn` until the expected file and the page are
  written again, in the same pull request.
- A release that ships a marked feature fails `learn` until the page is
  rewritten. The release's own pull request is where that happens.
- The docs site carries `docs/learn/` as a section, in place of ADR 0247's
  stub, which was kept "until it is written".
- Writing a page costs a marker per block and a region per quoted snippet.

## Checks

- **Checked against the accepted ADRs.**
  - Extends **0248**'s grammar; doesn't change its gate.
  - Uses **0141**'s pinned REC and **0176**'s composition.
  - Under **0247**'s link gate.
  - Respects **0004** (no `System.Uri`), **0060** (samples are layer-6
    hosts), **0224** (lockfiles, locked restore except the nightly's
    package check) and **0226** (one lockfile for every runtime).
  - No conflict.
- **Packages.** None added. `Microsoft.CodeAnalysis.BannedApiAnalyzers` is
  already registered (ADR 0004).
- **Layer ownership.** None shipped: `eng/learn.cs`, `samples/learn/`,
  `tests/Varve.Learn.Tests`.
- **Analyzer rule.** None. The rules are `eng/learn.cs`'s, as gates.
- **Open questions owned.** None.

## Amendment, 2026-11-25 — a sample's state leaves out the pinned vocabulary

Filed by the learning track, part (b) (#411, under #94), unaccepted (ADR
0066).

Point 3 says a sample writes "its dataset's head" as canonical N-Quads. From
chapter 3, a sample that binds REC's shapes holds REC 4 in the graph
`https://example.org/learn/rec` (ADR 0141, as amended on 2026-11-25). With
it, chapter 3's `expected/state.nq` is about 10 MB, of which 8 MB are REC's
55,126 statements, the same in every such sample, and canonicalising them
is most of the sample's run.

So a sample names the graph that holds the pinned vocabulary, and its state
leaves that graph out. Everything else in the head is written as before,
and the state is validated against REC 4 as before (point 8). The graph is
not left unchecked: it is loaded from `testdata/rec/<tag>/`, whose tag and
files `samples/learn/rec.pin` and `Varve.Learn.Tests` hold to the release,
and a change to it fails the sample's own load, since the shapes gate
refuses a shapes graph that does not load.

**The ledger.** The ruling is `StateLeavesOutThePinnedVocabulary`.
