# 0216 — The format inventory: fixtures written by the releases that introduced them, and the format gate

## Status

**Filed unaccepted by 1.0 readiness A (#129, its second deliverable),
2026-11-02** (ADR 0066). Acceptance is the maintainer's act on the pull
request. The plan it follows was approved on #129 on 2026-10-27, with the
answer that saved plans are versioned and positional, not read forever.

**Amends** [ADR 0166](0166-varve-management-and-the-vocabulary.md) by dated
amendment: a saved plan of an unknown format is refused with a message that
tells the operator to plan again.

Records decision set [`the-format-inventory`](../decisions/the-format-inventory.md).

## Context

#129 asks for format versioning under read-forever: an inventory of every
format with its version, the release that introduced it and its rule; a
fixture per format version, written by the release that introduced it, from
its published packages and not from current code; and a CI gate. Before this
ADR:

- the only release-written fixture was `testdata/derived-format-3`, built
  against the tag's source rather than its published package;
- the delta's fixture, `version-1.delta`, was written by hand;
- the event schemas' golden files were written by current code, so they
  could not show a change;
- the coordination records had no fixture: their tests wrote literal JSON;
- nothing checked that a version constant in the code had a fixture, or that
  a format's bytes stayed the same under the same version.

## Decision

**1. The inventory is a table,** in [`docs/formats/README.md`](../formats/README.md):
one row per format version, with its version, the constant in the code that
carries it, the release that introduced it, its rule, and its fixture or a
named gap. The rules are three:

- **read forever:** the log and its inline-set encoding, the object chunk
  header, the writer and reader records, the delta, the CloudEvents
  rendering and the event schemas; Replication's lineage record once it lands;
- **rebuilt:** derived formats 1 to 6, read or rebuilt from the log to the
  same results (ADR 0072, ADR 0139);
- **versioned:** the `varve apply` saved plan. It names positions, so a later
  release reads its format while it can and refuses an unknown one, telling
  the operator to plan again.

**2. A fixture per format version,** under `testdata/formats/`, written by a
generator kept as text beside it and run against the published packages of
the release that introduced the version, from nuget.org:

| Fixture | Release | Formats |
|---|---|---|
| `store-preview.1` | v0.1.0-preview.1 | log 1, inline set 1, derived 2 |
| `store-preview.2` | v0.1.0-preview.2 | derived 3 |
| `objects-preview.8` | v0.1.0-preview.8 | chunk header 1, writer record 1, derived 4 |
| `objects-preview.10` | v0.1.0-preview.10 | writer record 1 with `address` and `placement` |
| `objects-preview.11` | v0.1.0-preview.11 | reader record 1 |
| `objects-preview.15` | v0.1.0-preview.15 | reader record 1 with `maxDerived`, derived 5 |
| `delta-preview.6` | v0.1.0-preview.6 | delta 1 |
| `events-preview.7` | v0.1.0-preview.7 | CloudEvents rendering, event schemas |
| `events-preview.13` | v0.1.0-preview.13 | event schemas with management revisions |
| `plan-preview.10` | v0.1.0-preview.10 | saved plan 1 |
| `store-next` | this branch | derived 6, which no release has written yet |

- Dataset ids and clocks are fixed, so a release writes the same bytes on
  every run; each fixture has `SHA256SUMS` beside it.
- The object store's records and chunks are written by the release's own
  object-storage package over an in-memory store behind its own
  `IObjectStore`, so no service is needed. The plan proposed Azurite for
  these; the interface is public and unchanged from preview.8 to preview.15,
  so the release's code writes the same bytes without one.
- The expectations are written by each generator from what it issued (the
  operations of the store workload, the records it parsed, the plan it
  saved), not by reading a store back, so they depend on neither the old
  code nor the new.
- `testdata/derived-format-3` stays, for `Format3Tests`; `store-preview.2`
  holds derived 3 as the release that introduced it wrote it.

**3. The gate.**
- **Every read-forever fixture is read by current code** and compared with
  its expectations: the log, and its derived files, at every position; the
  delta record by record; the CloudEvents and the schemas byte for byte, by
  rendering the same inputs; the records field by field.
- **Every derived fixture is opened** by current code and gives the
  expectations at every position, by reading or by rebuilding.
- **A version with no fixture fails.** `eng/formats.cs` checks the table
  against the version constants in the code: a watched constant whose value
  is not its row's version, a row with neither a fixture nor a named gap, a
  fixture whose sums do not verify, and a fixture no row names. Its own
  fixtures, under `tests/fixtures/formats/`, prove each refusal on every
  pipeline run.
- **A format change without a new version fails.** Current code writes each
  fixture's workload, and the bytes must equal the fixture of the format's
  current version: the log as preview.1 wrote it, the chunks and the writer
  record as preview.10 wrote them, the reader record as preview.15 wrote it,
  the delta, the schemas and the events, and derived 6 as `store-next`
  holds it. One value is normalised, and the inventory names it: a reader
  record's `maxDerived`, which is the announcing build's.

**4. The named gaps:**
- derived format 1, which no release wrote (replaced within preview.1);
- inline set 2, which #114 builds and whose release writes its fixture;
- the lineage record, which the release that lands #175 writes.

## Alternatives considered

- **Fixtures written by current code at the old version's settings.** That
  is what the event goldens were: they show what current code writes, never
  what a release wrote. A consumer reads what the release wrote.
- **Azurite for the object fixtures,** as the plan proposed. It would add a
  service to a generator for bytes the release's own code writes in memory.
- **Every fixture regenerated at each release.** It would defeat the
  fixture: it is the introducing release's bytes, kept.

## Consequences

- A release that changes a read-forever format without a new version fails
  CI, at the test that writes the workload.
- A new format version needs a row in the inventory and a fixture, or its
  build fails at `formats`.
- The fixtures add about 1 MB to the repository, most of it the four object
  fixtures' 150 or so small objects each.
- The event goldens written by current code agree byte for byte with what
  preview.7 and preview.13 wrote, and the hand-written delta with what
  preview.6 wrote: none of them had drifted.

## Checks

- **Checked against the accepted ADRs.** Touches **0072** and **0139** (derived
  formats rebuilt, unchanged), **0135** (the chunk header, inventoried; its
  format is in ADR 0135 §2 only), **0160** and **0178** (reader records and
  `maxDerived`), **0166** (amended: saved plans), and the event ADRs **0143**
  and **0167**, whose outputs are now fixtures.
- **Layer ownership.** `testdata/`, the tests of each layer, `eng/formats.cs`,
  and one message in `Varve.Management`.
- **Analyzer rule.** None.
- **Open questions owned.** None.
