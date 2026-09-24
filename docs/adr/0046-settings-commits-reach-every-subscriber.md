# 0046 — Settings commits reach every subscriber; specification 1.2

## Status

**Accepted.** 2026-09-23. Amends [0016](0016-projection-contract-and-subscriptions.md)
by a dated amendment in that file. Moves the specification to **version 1.2**.

## Context

Specification §8 and ADR 0016 say that `Erasure` commits are delivered to every
subscriber regardless of filter, and say nothing of `Settings`. A `Settings`
commit has an empty delta (I4, ADR 0021), and §8 skips commits whose filtered
delta is empty — so under the text as written, **every filter drops every
settings change**, and only an unfiltered subscriber ever learns that erasure
mode was turned on or that the default access scope changed.

That is wrong for the reason 0016 gave for `Erasure`: a filter is about
relevance, not permission. A replica or a projection that filters to one graph
still has to know how the dataset it mirrors is configured, because settings
govern what happens to data arriving afterwards (ADR 0021). A subscriber that
missed erasure mode being turned on would treat the private terms that follow as
ordinary.

Milestone 4 is the first code against the specification, and three smaller
points surfaced with it. The maintainer decided all four together on
2026-09-23.

## Decision

**Specification version 1.2**, with a dated change entry, makes four changes.

1. **§8: `Settings` and `Erasure` commits are always delivered, regardless of
   filter.** They carry an empty delta and are delivered as themselves.
2. **§10, Determinism: the property holds for crash-free histories.** A torn or
   unclosed tail that recovery abandoned stays in `log/`, because the log is never
   rewritten and the storage contract has no truncate (ADRs 0018, 0040, 0045).
   Two machines that ran the same requests, one of which crashed, have the same
   readable log and different bytes.
3. **§1, the named-graph scope in `meta`: it is a declaration recorded in
   metadata and never enforced by the store; validators may enforce it.** The
   specification listed the field without saying which.
4. **§6, a note on equality.** When private terms exist, the source's comparer
   must hash by value for every class of id, because a readable private term is
   equal to a canonical term with the same value. Hashing then costs a
   dictionary lookup per term in erasure mode; with erasure mode off, a hash is
   the id.

## Alternatives considered

- **Leave §8 as written; a subscriber that needs settings subscribes
  unfiltered.** No specification change. Rejected: every filtered consumer would
  need a second, unfiltered subscription to stay correct, which is the
  "compose two feeds and get the arithmetic wrong" failure ADR 0022 refused for
  graph modes.
- **Deliver settings through a separate channel** — a property on the dataset,
  polled. Rejected by ADR 0021: settings travel with the log so that every copy
  agrees, and a consumer of the log should not need a second source to read it
  correctly.
- **Enforce the named-graph scope in the store.** Would make it a guarantee.
  Rejected: the store would then own a policy about which graphs a commit may
  touch, which is exactly what the validator hook exists to host (ADR 0017).

## Consequences

- **A subscriber's filter never hides configuration.** It still hides data.
- **The determinism property is stated honestly**, and the test for it runs
  crash-free sequences; the recovery property covers the crashed ones.
- **Commit metadata records a scope nothing checks.** A validator that wants it
  enforced reads it from the delta it is given — or, today, from its own
  configuration, since the scope is not yet passed to validators; widening what a
  validator may read would be a superseding change to ADR 0017.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0021–0045).
  Amends **0016** (the subscription rule), and touches **0021** (settings travel
  with the log), **0017** (validators may enforce the scope), **0018**, **0040**
  and **0045** (no truncate, so abandoned bytes remain), and **0022** and
  **0023** (the equality note). No conflict with any.
- **Layer ownership.** `Varve.Store`, **layer 4**.
- **Analyzer rule.** None.
- **Open questions owned.** None.
