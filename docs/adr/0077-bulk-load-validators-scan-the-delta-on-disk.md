# 0077 — Bulk load and validators: the overlay scans the delta on disk (Q3)

## Status

**Proposed — filed unaccepted by session 6a of #10, 2026-10-02** (ADR 0066).

Closes the specification's **Q3**, owned by ADR
[0013](0013-records-commits-and-bulk-load.md) with ADR
[0017](0017-validator-contract-and-overlay.md), due at milestone 6. Decided
here; **implemented in 6b** with the bulk loader (ADR 0076).

## Context

Q3: "the overlay of a multi-record commit does not fit in memory. Either
validators are disabled for bulk commits, or the overlay spills." ADR 0017
already chose scan-time merging over materialisation "so that spilling is an
implementation choice rather than a redesign", and ADR 0058 made validators the
dataset's gate — which a store policy disabling them for bulk commits would
open for anyone who calls the bulk path.

## Decision

**Validators receive `Overlay(pinned, δ)` and `δ`, as always, for a bulk commit
too.** The delta is the disk run of ADR 0076's step 2; the overlay scans it
through the synchronous blob read (ADR 0071) with the same cursor as any run,
merged at scan time with the pinned state's runs. **Nothing spills into memory,
and validators are never disabled.** `δ` is given to the validator as a quad
source over the same run, not as an array.

**Cost is proportional to the delta**, and the documentation of the bulk path
says so: a validator that scans `δ` reads it from disk; one that probes the
overlay reads the runs its patterns touch. A validator whose cost cannot be
paid at that size belongs in the asynchronous validation projection (ADR 0017,
§9), not at commit time.

## Alternatives considered

- **Disable validators for bulk commits.** Simple, and it makes the bulk path a
  way around every gate a dataset has (ADR 0058). Rejected.
- **Materialise the overlay.** Bounded by memory, which is the problem.
- **Validate the request in batches before the commit.** A batch sees part of
  the state the commit would produce, so a cross-batch constraint is unseen.

## Consequences

- **`δ` as a quad source** is a widening of how ADR 0017 hands a delta to a
  validator for bulk commits only; the `QuadDelta` form stays for ordinary
  commits. 6b states the exact member when it builds it, as an addition to the
  validator contract rather than a change to it.
- **Validation of a large load is slow and honest about it.**

## Checks

- **Checked against the accepted ADRs** (0001–0069). Closes **Q3** for **0013**
  and **0017**; touches **0058** (validators bound to the dataset stay binding),
  **0071** (the read), and **0076** (the run). No conflict with any.
- **Layer ownership.** `Varve.Store`, **layer 4**; the overlay is `Varve.Rdf`,
  layer 1 (ADR 0017).
- **Analyzer rule.** None.
- **Open questions owned.** Closes **Q3**.
