# The learning track, part (a): the gate, the samples, chapters 1 and 2

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#364](https://github.com/Hafeok/Varve/issues/364), part (a) of [#94](https://github.com/Hafeok/Varve/issues/94) |
| **Date** | 2026-11-19 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and last served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS9EfnKVVJ6zaNkU5z` |
| **Branch** | `claude/learn-a-94`, from `main` at c754bcba4f6ce605c4fe5e1c6ee91ab66351532a |

## The prompts

The session prompt:

> Session prompt: the learning track — Varve for SQL developers and DDD practitioners (#94)
> Paste into a fresh session on `Hafeok/varve`. Read `AGENTS.md` first and follow it in full. Then read #94 with its comments, ADR 0141 (RealEstateCore as the reference domain), and `docs/operator/` so the track does not repeat it.
> This prompt replaces the earlier learn-track prompt and its addendum of 2026-10-14. Where they differ, this one wins.
> What this is
> Most users think in SQL. What trips them here is not SPARQL syntax but modelling habits that are right in a relational database and wrong in a graph. Varve's fit with domain-driven design is its strongest argument and is shown nowhere.
> #94 is a learning track in `docs/learn/`, with one runnable sample per chapter in `samples/learn/`. It is in 1.0 (#15): "the learning track complete for every shipped feature". Nothing of it exists on `main` yet.
> What is already decided
> Do not re-open these. They are #94 and its amendments.
>
> * Eleven chapters and an appendix, in this order:
>    1. Rows as resources.
>    2. Links and patterns.
>    3. Schema without migrations.
>    4. Commits.
>    5. Deletion and history.
>    6. Temporal and audit.
>    7. CDC and the outbox.
>    8. Multi-tenancy and row-level security.
>    9. Named graphs.
>    10. From embedded to server.
>    11. Varve and DDD.
>    * Appendix: SQL habits that hurt here.
>    * Chapter 12, "Ontologies and inference", is a placeholder page. The Inference milestone (#95) writes it.
> * One domain throughout: a RealEstateCore portfolio that grows chapter by chapter (ADR 0141). The REC 4 files are pinned in `testdata/rec/`, and the portfolio generator exists.
>    * Chapters 1 and 2 use a hand-written slice small enough to read whole: one site, one building, two levels, a handful of rooms, a few assets and points.
>    * The full vocabulary and the generator appear from chapter 3 on.
>    * No REC term is used before the chapter that introduces it. A SQL developer never meets the Brick point hierarchy in chapter 1.
> * Every page quotes its sample's real output, and CI asserts it. A page never shows output nobody ran.
> * The maintainer reads chapters 1 and 2 as first reader, and the draft changes accordingly, before the rest is written.
>
> What each chapter teaches
> Each chapter sets the SQL habit beside the Varve way, on the same question.
>
> 1. Rows as resources. A room as a resource and its properties, against a `rooms` table.
> 2. Links and patterns. `hasPart`, `locatedIn`, `hasPoint`. "All rooms in a building" as a property path (`rec:hasPart+`) against a recursive CTE.
> 3. Schema without migrations. A new asset or point type is a write of data, not an `ALTER TABLE`. SHACL has shipped (#13), so this session writes the section: the REC 4 shapes as the schema, a commit refused by them, and the report.
> 4. Commits. A renovation as one commit: a room split in two, with its assets relocated.
> 5. Deletion and history. Retiring a point; what a retraction is. Erasure has not shipped (#203), so "a tenant's personal data erased" stays a marker that milestone 9 replaces.
> 6. Temporal and audit. "What did level 3 look like before the renovation?" and "who moved this asset, and when?", as as-of reads and a diff.
> 7. CDC and the outbox. Domain events from the events graph: an actuation command issued, an alarm raised, rendered as CloudEvents (#93). The chapter states the line plainly: sensor observations at rate go to an Eventhouse, joined on the point IRI, and are not written to Varve.
> 8. Multi-tenancy and row-level security. Tenants as agents and their collections (leases, apartments). Grants declared as code and applied with `varve apply`. Row-level access has shipped (#202, ADR 0238): a tenant reads its own rows, the facility manager reads all. Read #94's comment of 2026-11-06 for what it gives this chapter.
> 9. Named graphs. A graph per building and a graph per tenant collection, and why the graph is the unit of grants and of per-graph concurrency.
> 10. From embedded to server. The same portfolio in the embedded host and on a server. The server is the container image, with the console at `/ui/` (preview.22), and the typed client with its retry rule (ADR 0246).
> 11. Varve and DDD. Three bounded contexts, each a dataset with its own writer:
>    * Topology, built state-first: spaces, assets and points.
>    * Leasing, built state-first: collections and agents.
>    * Operations, built events-first: actuation commands and alarms.
>    * Cross-context references are IRIs, not joins across datasets.
>
> Appendix. With REC examples: a `type` column instead of classes, surrogate ids instead of IRIs, a junction table instead of a link.
> Rulings for this session
>
> 1. A reader must never expect one thing and get another (the maintainer, 2026-11-19).
>    * A chapter shows only what the release it is written against does. What has not shipped is a marker that names the issue, never prose that reads as if it works.
>    * Where Varve does less than a SQL developer would assume, the page says so where the assumption arises, not in a footnote.
> 2. The samples are built the way a user builds. Each is a small project that references the `Varve.*` packages and nothing internal. Propose in the plan whether they reference the repository's projects or the published packages, and how CI keeps them in step with `main`.
> 3. The samples start embedded. Chapters 1 to 9 need no server and no container, so a reader can run them with the SDK alone. Chapter 10 is where the server appears.
> 4. The samples hold the constraints. They build trimmed with no warning, and use no `System.Uri` where `Varve.Iri` is the type.
> 5. Every sample's data validates against the pinned REC 4 shapes, in CI, now that #13 has shipped.
> 6. The pages are on the docs site (#317): under its link gate, with Mermaid where structure is easier to see than to read. Prose is plain and precise, with no marketing tone, and written for someone who knows SQL well and RDF not at all.
> 7. Chapters 1 and 2 land first, alone. Then stop and ask the maintainer to read them. Do not write chapter 3 until he has answered.
>
> Plan questions
> Post the plan on #94 and wait for the maintainer's answer before writing a page.
>
> 1. Which REC terms each chapter introduces, as a table. No term before its chapter.
> 2. The hand-written slice for chapters 1 and 2, in full, as TriG.
> 3. For each chapter: the SQL it is set against, the one question it answers, and the Varve API or route the sample calls, with the spec section.
> 4. How a page's quoted output is asserted: where the expected output lives, how the page includes it, and which CI job fails when they differ. Say how this sits with ADR 0248's markers.
> 5. How the samples reference Varve (ruling 2), and how they pin the REC tag so CI fails if the pinned files change.
> 6. What each chapter needs that has not shipped, and its marker: at least erasure (chapter 5) and tier 2 inference (chapter 12).
> 7. The CI job for the samples: what it runs, its expected duration per OS, and what moves to the nightly.
> 8. The split into pull requests, and which carries the descriptor. Proposed:
>    * (a) the scaffolding, the CI job, chapters 1 and 2;
>    * (b) chapters 3 to 6;
>    * (c) chapters 7 to 9;
>    * (d) chapters 10 and 11, the appendix, the chapter 12 placeholder, and the descriptor that closes #94.
> 9. Any ADR, after checking `main` and every open pull request for the next free number, and any dated amendment to ADR 0141.
>
> Gate
>
> * Every chapter's sample builds and runs in CI, and its output equals what the page quotes.
> * Every sample's data validates against the pinned REC 4 shapes.
> * The term table holds: a check fails when a page uses a REC term before the chapter that introduces it.
> * The docs site's link gate passes with the track on the site.
> * No sample references anything internal, and each builds trimmed with no warning.
> * The maintainer has read chapters 1 and 2, and his changes are in.
>
> Not in this work
>
> * Chapter 12's content. The Inference milestone writes it.
> * The operator guide (#129) and the Web UI's own pages.
> * Any change to product code. If a sample shows a defect or a missing piece of public API, open an issue and mark the page; do not change `src/`.
>
> How this lands
>
> * Issues are the work log. Comment on #94 when you start. Each pull request has its own issue, a sub-issue of #94. Decisions, questions and blockers go on the issue when they arise. Work you find and don't do becomes its own issue at once.
> * You are a cloud session, not the maintainer's delegate (ADR 0239). File every decision unaccepted. Never write `accepted-by` on a decision set, and never land.
> * Never write the approval line, for any hash, anywhere, including in a report. Report a head as "head `<sha>`", with its hosted result after reading every failed job.
> * Descriptions and trailers. Each description carries one `Completes:` line, and the completing commit a `Closes #N` trailer. No closing keyword in a description. The last pull request's descriptor commit carries `Closes #94`.
> * The descriptor is format 2, at the next free preview number when it lands, with `accepted-by: mailto:emil@okkels-klein.dk`. Write its digests with `eng/release.cs --sbom` on the merge with `main`, and again whenever `main` moves. Never edit a descriptor that `main` already holds (#358), whatever a gate's message suggests.
> * ADR status lines read "Accepted — filed unaccepted by …".
> * Commits: as `Claude (Emil) <claude+emil@okkels-klein.dk>`, signed with the agent key. Never `git commit -s`. The co-author trailer is exactly `Co-Authored-By: Claude <noreply@anthropic.com>`; no commit message names a model.
> * Branches: one per pull request, named `claude/learn-<part>-94`. If a push to a new branch is refused, say so on #94 with the exact refusal and do not try another route.
> * No new package without a register entry and an ADR. Regenerate lockfiles with `dotnet run eng/lockfiles.cs`.
> * CI images only from `.github/ci-images.yaml`. No Docker Hub pulls.
> * When `main` moves, merge it in. Never rebase a pushed branch, and never force-push.
> * No suppression or downgrade of DD or VARVE rules. Never weaken a gate to make a test pass.
>
> Close-out
> `docs/sessions/2026-11-<name>.md` and a `docs/traceability/` record per pull request: the pull requests, issues opened and closed, the term table, each chapter's markers and the issue that removes each, the CI job's duration per OS, and what the maintainer changed in chapters 1 and 2. Update README Status, `docs/roadmap.md`, AGENTS.md's State, `eng/status.cs` and the changelog. Tick #94's boxes and #15's line only when the deliverable is complete as stated.

The answer to the plan:

> For the learning track session.
>
> The plan on #94 is accepted, with these answers to the five decisions:
>
> 1. Chapter 8 embedded: yes. Conditions: every type the sample uses is public API of a packable package, and eng/learn.cs's reference rule proves it. The page says, where the reader would expect otherwise, that embedded there is no authentication and the host enforces the scope it compiled; on a server the agent comes from the token and varve apply applies the same file. If the wiring is longer than a screen, open an issue for the missing public API and mark the page. Record on #94 that `varve apply` moves from chapter 8 to chapter 10, as an amendment to its deliverables.
> 2. The fixture: yes, as proposed.
> 3. The alarm modelling: as proposed, pending the maintainer's confirmation of the REC terms. Validate it against the REC 4 shapes first; if they refuse it, come back to #94 before writing chapter 7.
> 4. The default graph until chapter 9: yes.
> 5. ADR 0249: yes. It waits for #345 to land. Start pull request (a)'s work now, and open it once #345 is on main.
>
> Also:
> - main is c754bcba4f6ce605c4fe5e1c6ee91ab66351532a, not bb1d142. Start every branch from origin/main as it is then.
> - Chapter 1's slice gives a room's area and capacity as nested blank nodes. Write the page so a SQL developer reads that as "a value that is itself a small row", and say so on #94 when you ask the maintainer to read chapters 1 and 2. He may ask for a simpler first example.
> - Say on #94 why your checkout was 49 commits ahead of origin/main with an unlanded merge train, and confirm nothing from it reaches your branches.
> - Never edit a release descriptor that main holds (#358).

## The report

- **Issues.** The plan and its answers are on #94. Part (a) is #364, a
  sub-issue of #94. The correction about `origin/main` (a stale shallow
  tracking ref, not an unlanded train) is on #94.
- **The REC shapes changed the slice.** REC 4 constrains `rec:area` and
  `rec:capacity` with `sh:nodeKind sh:IRI`, so the planned blank nodes were
  refused (9 results). Each area and capacity now has an IRI of its own, and
  the slice conforms up to chapters 1 and 2. Reported on #94 before the
  pages were written.
- **The alarm modelling validates**, with `rec:value` required as an
  `xsd:string` on both `rec:ActuationEvent` and `rec:ExceptionEvent`.
  Reported on #94; it waits for the maintainer's confirmation of the terms
  before chapter 7.
- **The gate.**
  - `eng/learn.cs`: `--check`; `--run`, also with `--write`, `--trimmed
    <rid>` and `--packages <version>`; and `--fixtures`.
  - 15 fixtures under `tests/fixtures/learn/`, each failing for its own
    reason, and one passing.
  - Jobs `learn`, `learn-fixtures` and `test-learn` in `eng/ci.cs`.
  - In ci.yml, `learn` on Linux, Windows and macOS, and `learn (trimmed)`.
  - In the nightly, trimmed runs on Windows and macOS, and the samples built
    against the newest published packages.
  - `learn (ubuntu-latest)` and `learn (trimmed)` join the ruleset's
    required checks in `.github/repo-standard.yaml`. The Windows and macOS
    legs run on every pull request and become required after ten
    consecutive clean runs on `main` (the maintainer, 2026-11-19).
- **The samples.**
  - `samples/learn/01-rows-as-resources` and `02-links-and-patterns`, each a
    layer-6 console project that names `Varve.Store`, `Varve.Turtle` and
    `Varve.Sparql.Store` by `VarveReference`.
  - Each builds with the trim, AOT and single-file analyzers, and with
    `System.Uri` banned.
  - Each publishes trimmed for linux-x64 with no warning, and the trimmed
    executable prints the same output.
  - Built against the published `0.1.0-preview.22` packages, both print the
    same output too, so chapters 1 and 2 show only what that release does.
- **The data.** `tests/Varve.Learn.Tests` checks the pin, then validates
  the slice for chapter 1 and for chapter 2, and both samples' final state,
  against REC 4 composed as the REC gate composes it, in about 1.5 s.
- **The pages.**
  - `docs/learn/README.md`, `terms.md`, `01-rows-as-resources.md` and
    `02-links-and-patterns.md`, with three Mermaid diagrams.
  - The site carries `docs/learn/` as its "Learning track" section, in
    place of ADR 0247's stub. Link gate: 564 pages, 0 broken. The full site
    build draws the diagrams with 0 findings.
  - One marker, `#95`, on chapter 2: `owl:inverseOf` is not applied by
    inference yet. Chapter 12 is not linked until it exists.
- **Durations, measured in this container (Linux x64).** `learn --run`,
  both samples: about 9 s after the first build, which takes about 25 s.
  The trimmed run takes about 19 s. The `learn` job's time on Windows and
  macOS is recorded from its first hosted run.
- **The ADR.** 0249, filed unaccepted, with its decision set. It extends
  0248 (#345), and the pull request opens once #345 is on `main`.
- **Nothing in `src/` changed.**

## After the pull request opened (2026-11-20)

- **CI fixes.**
  - `learn (trimmed)`: a global `-r` broke CI's locked restore of `src/`
    (NU1004), so a trimmed sample's runtime is now set in its props from
    `VarveTrimFor`.
  - `docs site`: Mermaid's stadium shape draws differently on each build,
    so chapter 1's diagram uses rectangles. The cause is filed as #386.
- **The track teaches RDF and SPARQL, not Varve specifics.** The session's
  user, reading chapters 1 and 2, said:

  > The chatpers are written with Varve in mind but we are actually
  > teaching SPARQL and RDF and not Varve specifics

  Asked which way to go, the user chose "Generic RDF/SPARQL first".
  - Each chapter's queries now live in `.rq` and `.ru` files that the
    sample runs and the page quotes, so the pages show plain SPARQL, not
    C#.
  - The prose says "RDF" and "an RDF store" where it said "Varve".
  - The Varve wiring (the dataset, the loader, commits and their
    positions, the packages) moved into one section, "In Varve", at the
    end of each chapter.
  - The chapter list is unchanged.
- **Chapters 1 and 2 approved.** On 2026-11-20 the session's user wrote,
  of the chapters as they stand at `38b33426`:

  > Great all chapters are approved

  No further changes were asked for. The first reading's change is the one
  above: RDF and SPARQL first, with Varve in its own section.
- **Hosted result at `38b33426897e54edec571a17e4bcaa679d5c66d7`.** Of 46
  check runs, 44 passed and `auth (providers)` was skipped by its own
  condition. The one failure was `agent review`, waiting for the
  maintainer's approval of that head. The `learn` job took 1 min 14 s on
  Ubuntu, 2 min 14 s on Windows and 2 min 36 s on macOS, and
  `learn (trimmed)` took 1 min 26 s.
