# `proof/` is reserved in storage format 1

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#340](https://github.com/Hafeok/Varve/issues/340); for [#292](https://github.com/Hafeok/Varve/issues/292); the shipper's session told on [#166](https://github.com/Hafeok/Varve/issues/166#issuecomment-6978989135) |
| **Date** | 2026-11-18 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239) |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYPUMnLqwEBi4fBQ1tR` |
| **Branch** | `claude/reserve-proof-340`, from `main` at bb1d142 |
| **ADRs** | [0216](../adr/0216-the-format-inventory.md), amended 2026-11-18, filed unaccepted |

## The prompt

The session's prompt covers three pull requests; this record is step 2's.

> Session prompt: three small 1.0 items — reserve `proof/` (#340), absent secrets in repo-standard (#327), the idle-fleet test (#332)
> Paste into a fresh session on `Hafeok/varve`. Read `AGENTS.md` first and follow it in full, then #340, #327 and #332 with their comments.
> Three independent, small changes. One pull request each, in this order. None carries a release descriptor. They may land as one train.
>
> Step 1 — #327: a declared secret may be absent
> `main`'s weekly `repo-standard` job is red because the `auth-providers` environment declares five secrets that do not exist yet (three `VARVE_ENTRA_*`, two `VARVE_GOOGLE_*`). The maintainer has not provisioned those tenants, and ADR 0223 already says the provider leg skips when none of its identifiers are present.
> The maintainer's ruling of 2026-11-17: the five values may be declared as absent.
>
> * The tool's schema types an environment's secrets as a plain list of names, so a name cannot be marked absent today. Extend it so a secret can be declared with its name and that it may be absent.
> * A secret declared so and missing is reported as "declared, absent", and the run is not red for it. A secret declared without that marking and missing is still red.
> * repo-standard never reads or writes a secret's value. That does not change.
> * Mark the five `auth-providers` values, and nothing else.
> * Fixtures: declared and present; declared as may-be-absent and missing; declared plainly and missing (red); an undeclared secret that exists (as today).
> * If the schema change needs a decision, file it unaccepted as a dated amendment to the ADR that holds repo-standard's schema.
>
> Step 2 — #340: reserve `proof/` in storage format 1
> #292 (accepted as a proposal on 2026-11-18, for a minor after 1.0) will add a `proof/` directory beside `log/` and `derived/`. The format freezes at 1.0, so the name is reserved now.
>
> * Find out first, and post it on #340: what each reader of a dataset directory does today with an unknown sibling of `log/` and `derived/`, on the file backend and on an object store: open for read, open for write, a follower, backup and restore, `varve storage`, and the format inventory's gate. The shipper is being built on #338; say what you found there and tell that session on #166, and do not change its branch.
> * Reserve the name in `docs/spec/storage-format.md` and the format inventory (ADR 0216): 1.0 writes nothing there; a reader ignores it and never deletes it.
> * A read-forever fixture: a format 1 dataset with a `proof/` directory holding objects this build does not know. It opens for read and for write, follows and restores, and the objects are byte for byte the same afterwards.
> * A dated amendment to ADR 0216, filed unaccepted.
> * Build no tile, checkpoint, tree arithmetic or route. That is #292.
>
> Step 3 — #332: an idle fleet's system dataset does not move
> ADR 0244's idle collection runs only when the whole server is idle, and the system dataset counts toward that. The delegate read the code and found that nothing commits to the system dataset on a timer; nothing asserts it.
>
> * A test: a fleet of at least a writer and a follower, with no requests, over many `Fleet:FollowInterval` ticks under a controlled clock. The system dataset's head position does not move.
> * On the same fleet: after a checkpoint and the idle wait, the idle collection runs.
> * Sized for the land gate, with no real waiting. Report its duration per OS.
> * If the test fails, that is a finding: stop, post it on #332 and #227, and do not change product code to make it pass without the maintainer's answer.
>
> How this lands
>
> * Issues are the work log. Comment on each issue when you start it. Work you find and don't do becomes its own issue at once.
> * You are a cloud session, not the maintainer's delegate (ADR 0239). File every decision unaccepted. Never write `accepted-by` on a decision set, and never land.
> * Never write the approval line, for any hash, anywhere, including in a report. Report a head as "head `<sha>`", with its hosted result after reading every failed job.
> * Each description carries `Completes: #N` for its issue, and the completing commit `Closes #N`. No closing keyword in a description.
> * ADR status lines read "Accepted — filed unaccepted by …".
> * Commits: as `Claude (Emil) <claude+emil@okkels-klein.dk>`, signed with the agent key. Never `git commit -s`. The co-author trailer is exactly `Co-Authored-By: Claude <noreply@anthropic.com>`; no commit message names a model.
> * Branches: one per pull request, named `claude/<what>-<issue>`. If a push to a new branch is refused, say so on the issue with the exact refusal and do not try another route.
> * When `main` moves, merge it in. Never rebase a pushed branch, and never force-push.
> * No suppression or downgrade of DD or VARVE rules. Never weaken a gate to make a test pass.
>
> Close-out
> `docs/sessions/2026-11-<name>.md` and a `docs/traceability/` record per pull request: the pull requests, issues closed, what each reader does with an unknown directory, the test's duration per OS, and anything deferred as a linked issue.

## The report

**What each reader does today with an unknown sibling of `log/` and
`derived/`** (posted on
[#340](https://github.com/Hafeok/Varve/issues/340#issuecomment-6978986842)):
none of them looks. Each names what it reads.

| Reader | File backend | Object store |
|---|---|---|
| Open for read | no read-only file open | lists `log/`, `derived/`, `derived/extents/`, `readers/`; reads `writer` |
| Open for write | creates and lists `log/` and `derived/` only | as for read, plus `probe/<id>`, `writer`, `readers/<id>`; deletes only under `derived/` and replaced chunks under `log/` |
| A follower | none (only `ObjectStorage` tails) | probes the next `log/` slot, reads `derived/` |
| Backup and restore | a directory copy carries `proof/`; `ShipAsync` does not, and accepts a target holding only `proof/` | `ShipAsync` into a directory, or the store's replication |
| `varve storage` | n/a | catalog, `derived/extents/`, `readers/` |
| The format gate | reads the table and each fixture's `SHA256SUMS`, no dataset | same |
| Deleting the dataset | removes the directory, `proof/` with it | removes every object under the prefix, `proof/` with it |

**The shipper (#338, head b46d4f4, read only):** lists only `log/` on both
sides and reads `writer`, `lineage` and the manifest; judges "target not
empty" by those, so a target with only `proof/` is empty; deletes only
replaced chunks. Extent sets and retired-set deletion are not built yet; its
session was asked on #166 to list `derived/extents/` there, never the prefix.

**What changed:** no code. `docs/spec/storage-format.md` §2.1 reserves
`proof/`; the inventory gains the rule *reserved* and a row for `proof/`
with the fixture `proof-reserved`; ADR 0216 has a dated amendment with the
decision `ProofIsReserved`, **filed unaccepted**.

**The fixture** `testdata/formats/proof-reserved/proof/`: a tile-shaped
object, a checkpoint and its signature as #292 proposes them, a version
marker of a format nobody has defined, an empty object and a deep path,
from `generator/generate.cs`, which references no Varve package and writes
the same bytes on every run (checked twice).

**The test** `Formats.ReservedProofTests`, two facts:
- files: `store-preview.1`'s log and derived files with `proof/` beside them,
  opened, read at every position, written, checkpointed and maintained with
  merges on; shipped by `ShipAsync` to a target holding its own `proof/`;
  the shipped copy opened; the directory copied and the copy opened and
  written. `proof/` is byte for byte the fixture's, with nothing added, in
  the source, the target and the restored copy.
- an object store: `objects-preview.15`'s objects with `proof/...` beside
  them, opened for read, then for write with a follower; written,
  checkpointed, maintained and the log consolidated; the follower advanced
  to the head; shipped into a directory and that opened. `proof/` is
  unchanged in the store throughout.

**Run here:** the `Formats` tests, 19 passed, about 2.8 s; `licence-headers`,
`decision-sets`, `formats`, `formats-fixtures` pass. The test fails as it
should when the file open is made to delete one `proof/` object (tried
locally, reverted). The build is red on `CS0618` for `ProofIsReserved` until
the maintainer accepts it (ADR 0066); the tests were run here with `CS0618`
not an error on this machine only.
