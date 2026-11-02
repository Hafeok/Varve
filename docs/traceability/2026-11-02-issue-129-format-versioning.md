# Format versioning under read-forever: the inventory, fixtures from releases, and the gate

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#129](https://github.com/Hafeok/Varve/issues/129) (its second deliverable); [#175](https://github.com/Hafeok/Varve/pull/175) and [#114](https://github.com/Hafeok/Varve/issues/114) referenced for their gaps |
| **Date** | 2026-11-02 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRzUotGnNtdpHy2K62` |
| **Branch** | `claude/adoring-keller-42dw5z-step4`, from `main` |
| **ADRs** | [0216](../adr/0216-the-format-inventory.md), [0217](../adr/0217-coordination-records-read-conservatively.md) |

## The prompt

The session's prompt and the plan's approval are quoted in
[step 2's record](2026-10-27-issue-146-verified-derived-data.md). The
brief's step 4, verbatim:

> **4. Format versioning under read-forever** (#129's second deliverable).
> - **The inventory in `docs/formats/`,** with each format's version, the release that introduced it, and its rule:
>   - **read forever:** the log; `delta-format: 1`; the CloudEvents rendering; the event-schema generator's output; the inline-set encoding; the coordination records (the writer record, reader records, and Replication's lineage record when it lands);
>   - **rebuilt, not read forever:** derived formats 1–5 (ADRs 0072 and 0139).
> - **A fixture per format version,** written by the release that introduced it. For versions already shipped, generate the fixture with that release's published packages, not with current code.
> - **The CI gate:**
>   - every read-forever fixture is read by current code and compared with stored expectations;
>   - every derived fixture is opened and reaches the same results, by reading or by rebuilding;
>   - a version with no fixture fails the gate, and so does a format change without a new version;
>   - an unknown version of a coordination record is treated conservatively, never ignored.

The maintainer's answer on saved plans, in the plan's approval, verbatim:

> 6. Saved plans aren't read forever. They're versioned and positional. An unknown version is refused with a problem telling the operator to re-plan. The inventory lists them under that rule.

And on the order of work, 2026-11-02: "Then step 4, format versioning, as planned."

## The report

### What was built

- **Eleven fixtures under `testdata/formats/`,** each written by a generator
  kept beside it and run against the published packages of the release that
  introduced the format version: preview.1 (log 1, inline set 1, derived 2),
  preview.2 (derived 3), preview.6 (delta 1), preview.7 (CloudEvents, event
  schemas), preview.8 (chunk header 1, writer record 1, derived 4),
  preview.10 (writer record with `address` and `placement`; saved plan 1),
  preview.11 (reader record 1), preview.13 (schemas with management
  revisions), preview.15 (reader record with `maxDerived`, derived 5), and
  this branch for derived 6. About 1 MB.
- **The object store's fixtures without a service:** the release's own
  object-storage package over an in-memory store behind its public
  `IObjectStore`, unchanged from preview.8 to preview.15.
- **The tests:** every fixture read by current code at every position, the
  delta record by record, the schemas and events byte for byte, the records
  field by field; and current code writing each workload to the bytes of the
  current version's fixture.
- **`eng/formats.cs`,** the inventory against the code, with seven fixtures of
  its own.
- **ADR 0217's rule in code:** an unknown writer-record format is never
  written on, and a follower reads on; an unknown trigger or reason is held,
  kept and never acted on. Tested on preview.10's record with its format or
  values changed; the second test fails against the previous code.
- **A saved plan of an unknown format** now tells the operator to plan again.

### What the work found

- **Nothing had drifted.** The log current code writes is byte for byte
  preview.1's; the chunks and writer record, preview.10's; the reader record,
  preview.15's but for its `maxDerived` value; the event goldens that current
  code had written, preview.7's and preview.13's; the hand-written delta,
  preview.6's.
- **An older instance stopped on a newer writer record's value.** Before
  ADR 0217, a preview.8 or preview.9 instance threw on a record a preview.10
  writer released for `placement`, follower or writer alike.

### The gates

- `Varve.Store.Tests` 439 (353 run, 86 skipped for services), `Varve.Delta.Tests`
  15, `Varve.Events.Tests` 158, `Varve.Management.Tests` 59,
  `Varve.Server.Tests` 135 (121 run): none failed.
- The build is red only on `CS0618`, ADR 0216's and 0217's rulings not yet
  accepted.
