# Topic — the log and projection model

> **The transcript is held by the maintainer**, who attaches it to this file.
> What follows is a summary naming the decisions the conversation produced, and
> it is deliberately not presented as a substitute for the transcript. A
> summary naming decisions is worth more than nothing and is honest about being
> less than a record; claiming a record exists when it does not would be worse
> than either ([ADR 0033](../adr/0033-commit-traceability.md)).

| | |
|---|---|
| **Issues** | [#5](https://github.com/Hafeok/Varve/issues/5), [#18](https://github.com/Hafeok/Varve/issues/18) |
| **Where** | Outside this repository |
| **Tool** | Claude Code and the Claude apps |
| **Model** | Claude Opus 5 |
| **Produced** | `docs/spec/log-and-projection-model.md`, ADRs 0010–0023 |

## What was being decided

`Varve.Store` is the part of the system whose shape cannot be changed later
without a format migration. Before any byte was written, the conversation had
to settle what a commit is, how concurrency works, what time travel survives,
and whether erasure can exist at all in a log that is never rewritten.

The brief posed these as *tensions* rather than as questions with known
answers. `docs/roadmap.md` §2 tabulates where each one landed.

## Decisions it produced

| Question the brief posed | Where it landed |
|---|---|
| Log growth, compaction, and what time travel survives it | **[0015](../adr/0015-checkpoints-and-reads.md)** — there is no compaction in the destructive sense. Checkpoints bound read cost; the log is retained. |
| Blank node identity across transactions and across time | **[0012](../adr/0012-term-dictionary-and-id-scheme.md)** — store-scoped identity allocated at commit, request labels request-scoped. The external form stayed open as **Q1**. |
| Retraction of a quad that is not there; re-assertion | **[0010](../adr/0010-commit-model-and-effective-deltas.md)** — neither an event nor an error. The log records the *effective delta*. |
| SPARQL Update semantics, and one request as one commit | **[0005](../adr/0005-store-is-sparql-free.md)** with 0010 and 0011 — Update is layer 5; it evaluates `WHERE` against a pinned position and submits the delta as one commit. |
| Bulk load as one logical commit | **[0013](../adr/0013-records-commits-and-bulk-load.md)** — a commit is one or more *records*, closed by a flag. **Q2** and **Q3** stayed open. |
| Single writer versus optimistic concurrency | **[0011](../adr/0011-concurrency-single-sequencer.md)** — both. One sequencer, with an optional expected position per request. |
| Managed storage engine for the projections | **Left open**, with `docs/research/managed-storage-engines.md` narrowing it and [0018](../adr/0018-storage-abstraction.md) stating what the contract requires. Due milestone 6. |
| GDPR-style hard deletion in an append-only model | **[0019](../adr/0019-erasure-by-crypto-shredding.md)**, superseded by **[0023](../adr/0023-erasure-and-access-requests.md)** — crypto-shredding, opt-in per dataset, access by key id. **Q4, Q5, Q8, Q9** stayed open. |
| Commit-time validation cost against write latency | **[0017](../adr/0017-validator-contract-and-overlay.md)** — the validator sees the overlay of the pending delta on the pinned state and nothing else; the hook is inside the sequencer, so validation *is* write latency by construction. |
| Incremental SHACL — which shapes are maintainable | **Not addressed.** Due milestone 8. |

Also produced: [0014](../adr/0014-header-chain-and-divergence.md) (header chain
and divergence detection), [0016](../adr/0016-projection-contract-and-subscriptions.md)
(projection contract), [0021](../adr/0021-dataset-settings-as-a-commit-kind.md)
(settings as a commit kind), [0022](../adr/0022-quad-source-term-handle.md) (the
quad source over an opaque term handle).

## The shape of the conversation, as far as it can be stated

Two things about it are visible in the artefacts and worth recording, because
they are what a reader of the ADRs alone would miss.

**Nine questions were left open on purpose.** Q1 to Q9 are not omissions. Each
is a question whose honest answer depends on a measurement that did not exist
yet, and each is recorded against the ADR it falls out of with a due milestone.
The alternative — deciding them anyway — would have produced ADRs with
confident *Decision* sections and no evidence, which is the failure mode the
revisit-condition mechanism exists to avoid.

**Two ADRs were superseded before any code existed.** 0019 by 0023 and,
shortly after, 0020 by 0028. Both supersessions were the specification being
written carefully enough to find that the earlier decision did not survive
contact with the detail.

## What this summary cannot tell you

Which options were discussed and never reached an ADR; the order in which the
tensions were taken; and what the maintainer rejected on the way. The ADRs'
*Alternatives considered* sections carry the most important part of that — the
options that lost, and why — which is precisely why that section is mandatory
here.
