# 0217 — Coordination records read conservatively: an unknown format is never written on, an unknown value never acted on

## Status

**Accepted — filed unaccepted by 1.0 readiness A (#129, its second deliverable),
2026-11-02** (ADR 0066). Acceptance is the maintainer's act on the pull
request. The plan it follows was approved on #129 on 2026-10-27.

**Amends** [ADR 0136](0136-one-writer-by-an-epoch-and-a-log-slot-fence.md) by
dated amendment: what an open makes of a writer record it does not fully
read.

Records decision set [`coordination-records-read-conservatively`](../decisions/coordination-records-read-conservatively.md).

## Context

The writer record (ADR 0136) and the reader records (ADR 0160) are read by
every instance that opens a dataset in an object store, including instances
of older releases during an upgrade. Before this ADR, `WriterRecordFormat.Read`
threw `InvalidDataException` out of every open on:
- a record of an unknown `format`, which is conservative but was not stated,
  and stopped a read-only follower as well as a writer;
- an unknown `trigger` or `released` value under format 1. That is how a
  preview.8 or preview.9 instance failed on a record a preview.10 writer
  released for `placement`: format 1 did not change, a value was added.

Reader records already held everything when unreadable: a record of another
format, or one that does not read, keeps every catalog version (ADR 0160).

## Decision

**1. An unknown writer-record format is never written on.**
`WriterRecordFormatUnknownException` names the record's format and the one
this version reads. An open for write and a takeover refuse with it before
anything is written: no claim, no fence, no takeover. A read-only open goes
on as a follower that knows no writer, so it reads the log while it reads,
and forwards writes to nobody (ADR 0168).

**2. An unknown value under a known format is held, never acted on.** A
trigger or a release reason this version does not know reads as
`ClaimTrigger.Unknown` or `ReleaseReason.Unknown`, with the text the record
had. A claim that rewrites the record writes that text back as it was read.
Nothing is inferred from it: an unknown release is a release, so the next
claim follows it, but no placement handover or other reason is acted on
(`OpenDatasets` hands over on `Placement` only; `Fleet` forwards to a writer
that is not released only).

**3. A reader record of an unknown format holds everything,** as ADR 0160
has it: every catalog version, and derived format 4.

**4. Replication's lineage record follows the same rule** when it lands
(#175): a format this version does not read is never written on, and an
unknown value is held and never acted on.

## Alternatives considered

- **Throw on an unknown value, as before.** It stops every older instance,
  follower and writer alike, on a value it does not need to understand to
  read the log.
- **Ignore an unknown format and read what can be read.** A writer would
  then rewrite a record it does not understand, losing what a later version
  put there, and could claim against rules it does not know.
- **Map an unknown reason to `Shutdown`.** The record would be rewritten
  with a reason it never had.

## Consequences

- During an upgrade, an older follower keeps reading a dataset whose writer
  record a newer release wrote; an older writer refuses to write it, by
  name, and says to upgrade.
- `ClaimTrigger` and `ReleaseReason` gain `Unknown`; the object-storage
  package gains `WriterRecordFormatUnknownException`. `varve lease`
  prints an unknown value as `unknown`.
- Tests on preview.10's own record, with its format or its values changed as
  a later release could, show both rules; the second fails against the
  previous code.

## Checks

- **Checked against the accepted ADRs.** Touches **0136** (amended), **0160**
  (reader records, unchanged), **0163** (placement, which an unknown reason
  never triggers) and **0168** (forwarding, which a follower that knows no
  writer does not do).
- **Layer ownership.** `Varve.Store.ObjectStorage`.
- **Analyzer rule.** None.
- **Open questions owned.** The lineage record's rule is agreed with #175
  before it lands.
