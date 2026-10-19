# 0162 — Storage operations: `varve storage`, the readers command, readiness, and the operator guide

## Status

**Accepted — filed unaccepted by milestone Extent maintenance of #110,
2026-10-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the Extent maintenance plan,
2026-10-19:
- readiness reports retained bytes and never turns unready because of them;
- the command line's read commands take an explicit `--unannounced`.

Builds on [ADR 0160](0160-extent-deletion-by-reader-announcements.md), whose
events and figures this ADR puts in the operator's hands.

Records decision set [`storage-operations-and-the-readers-command`](../decisions/storage-operations-and-the-readers-command.md).

## Context

The deletion rule (ADR 0160) keeps what a reader record holds, and a dead
reader's record holds it until an event removes it. The operator needs to
see three things, and act on one:
- what a dataset's chain is;
- what the store keeps that no catalog names;
- which record keeps it;
- and then forget a reader that is gone.

`varve lease` (ADR 0136) is the precedent: it reads the writer record straight
from the store, as the caller's identity, and its takeover names the epoch it
saw.

## Decision

**1. `varve storage <address>`** reads a dataset in an object store, without
changing anything, and prints:
- the catalog's version and position;
- the extent sets per permutation — every set has one extent per order, so
  each permutation has as many — with each order's bytes;
- each set's window and bytes;
- the sets no catalog names that the store still keeps, and their bytes;
- each reader record and what it retains.

A set no catalog names is counted for a record that announces any version
after the set's sequence and before the current one: the deletion rule's
test, with the current version as the bound (ADR 0160, point 1).

**2. `varve storage readers list <address>`** prints every reader record:
- its id and its interval, or that it is of a format this build does not
  read and holds every version;
- whether it announces or was forgotten;
- its holder and when it was written, by the host's clock;
- the bytes it retains.

**3. `varve storage readers forget <address> --reader <id> --oldest <v>
--newest <v>`** forgets one record.
- **Guarded by what list showed.** It acts only while the record still
  announces that interval, as `varve lease --take --epoch` acts only on the
  epoch it names. The record is replaced by a tombstone against the version
  read, then deleted, so a reader that announced since is not forgotten by
  mistake.
- **Authorised by the store.** Its authorisation is the caller's identity,
  which must be able to write `readers/` (ADR 0137). It is not an HTTP route.
- **No time is consulted.** The operator is the one who decided the reader is
  gone.
- **Afterwards.** The writer deletes what the record held at its next
  maintenance round. A reader that was in fact alive fails the reads that find
  an extent gone with `reader-forgotten`, and attaches again (ADR 0160).

**4. The read commands attach to an object store, announced.**
- `varve info`, `query`, `export` and `feed` accept a dataset in an object
  store: `https://{account}.blob.core.windows.net/{container}/{dataset}`,
  Azurite's development account on a loopback address, or
  `s3://{bucket}/{dataset}` with `--endpoint` and `--region`.
- They open it read-only, claim nothing, and announce in a reader record of
  a fresh id, which they delete when they end.
- **`--unannounced`** is the explicit read for an identity that may only
  read: no record, and a read that finds an extent gone fails with
  `read-unannounced-extent-gone`, never a wrong result.
- A command that writes is refused, with `writer-superseded`.

**5. Readiness reports retention, and never turns unready because of it.**
- `/health/ready` gains `retention`, for each dataset that has reader
  records: the catalog version, the sets, the unnamed sets the store still
  keeps, their bytes, and each record with its interval and the bytes it
  retains.
- The figures are as of the last time the writer read the records.
- Readiness's verdict is unchanged: retention is for the operator to watch,
  and to act on with `forget`.
- The `not-ready` problem carries the same member.

**6. The operator guide replaces the checkpoint budget** of #92 with:
- the merge policy and its bound;
- write amplification, measured;
- retained bytes, and how to read them;
- the readers command;
- the follower's write access to `readers/`, for Azure and S3 (ADR 0160).

**7. `varve storage verify`** (#117) is not built in this milestone. When it
lands, it gains two checks:
- every extent a catalog names exists;
- no reader record names a catalog version that no longer exists.

## Alternatives considered

- **An admin route to forget a reader.** The writer would then delete on an
  HTTP caller's word. The store's identity is already the authority over the
  store's objects, as for `varve lease --take`.
- **Forget by id alone.** A reader that announced between the operator's
  list and the forget would be forgotten unseen. The interval is the guard,
  as the epoch is for a takeover.
- **Turn readiness unready past a retention threshold.** A threshold of bytes
  is a judgement the operator makes. An orchestrator that restarts the
  writer on readiness would not remove a dead follower's record.

## Consequences

- `Varve.Server` gains `varve storage` and `varve storage readers list` and
  `forget`; `--unannounced`, `--endpoint` and `--region` on the read
  commands; `retention` on readiness.
- The settings gain `Varve:ObjectStorage:ReaderId` and
  `Varve:Store:ExtentMerges:{Enabled,FanOut,MaxSetBytes}`.
- `docs/operator/` changes as point 6 says.
- **Not in this milestone:**
  - `varve storage verify` waits for #117 (point 7);
  - multipart uploads stay a later decision. Until then a merge whose output
    would pass the store's single-upload limit is refused before it starts
    (ADR 0158).

## Checks

- **Checked against the accepted ADRs** (0001–0147). Touches:
  - **0105**: the command line gains commands and a third kind of dataset
    address;
  - **0113**: readiness gains a field and no state;
  - **0115**: two settings;
  - **0119**: the `not-ready` problem gains a member;
  - **0136**: the takeover's guard, as the precedent.

  No conflict.
- **Layer ownership.** `Varve.Server`, layer 6. What it reads and writes is
  `ObjectStorage`'s public API, layer 5.
- **Analyzer rule.** None.
- **Tests.**
  - The settings' known-keys test.
  - `ProblemCatalogueTests` against `docs/problems/`.
  - `ObjectExtentMaintenanceTests` for `ForgetReaderAsync`'s guard and
    `ReadReportAsync`.
  - `StorageCommandTests` for the commands' output.
