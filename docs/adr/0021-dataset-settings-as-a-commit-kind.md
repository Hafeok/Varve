# 0021 — Dataset settings as a commit kind

## Status

**Accepted.** 2026-09-21.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
§1 (*Settings*), T5 and I4.

## Context

A dataset has configuration that changes its behaviour: whether erasure mode is
on, and what an access request defaults to covering (ADR 0023). Both are
decisions somebody makes, at a time, for a reason — and both change what the
store does to data that arrives afterwards.

§2 is what makes the storage question non-obvious. The dataset directory is
copied, backed up, synchronised and checked into version control by tools that
know nothing about Varve. A setting kept beside the log rather than in it — a
`settings.json`, a row in a sidecar database, a value in an environment
variable — travels differently from the data it governs, or does not travel at
all. Two copies of one dataset would then disagree about whether erasure mode is
on, and nothing would detect it.

There is a second question underneath: a setting that can be changed has a
history, and "when did erasure mode get turned on, and by whom" is exactly the
kind of question this store exists to be able to answer about everything else.

## Decision

**Settings are a commit kind.** `kind ∈ {Data, Erasure, Settings}`.

- A `Settings` commit goes through the sequencer like any other, with **agent
  and cause** in its metadata and a position in the log.
- The dataset's settings are **a fold over the `Settings` commits** up to a
  position — so settings are a function of `L[1..P]` exactly as the graph is,
  and `S(P)` remains a pure function of the log (§3).
- A `Settings` commit has an **empty delta** and is exempt from I4, alongside
  `Erasure`.
- **Erasure mode cannot be turned off while any private entry exists in `D`.**
  The sequencer refuses. Turning it off would leave entries nothing can ever
  decrypt and nothing can ever erase — readable only while the key survives,
  with no mechanism left to destroy it.

Settings therefore **travel with the log**. Every copy of a dataset agrees about
them, because they are in the bytes that get copied, in order, and every change
has an agent and a position.

## Alternatives considered

- **A configuration file in the dataset directory.** The obvious answer, and
  what almost every store does. Rejected by §2: a file beside `log/` is copied
  by some tools and not others, can be edited by anyone with the directory, has
  no author and no timestamp anyone can trust, and leaves two copies of a
  dataset able to disagree about erasure mode with nothing to detect it. It also
  puts a mutable, editable file inside a directory whose entire design rests on
  the log being the only source of truth.
- **Settings outside the dataset entirely** — a host configuration, an
  environment variable, a connection-string option. Worse in the same direction:
  the dataset becomes unable to describe itself, and moving it to another host
  changes its behaviour silently. It also makes erasure mode a deployment
  decision rather than a dataset decision, which is precisely the wrong owner.
- **Settings as ordinary `Data` commits** in a reserved graph. Tempting: no new
  commit kind, and settings become queryable with everything else. Rejected
  because it makes configuration indistinguishable from data to every consumer —
  a subscriber's filter could drop it, a projection would have to know which
  graph is special, and a user could change erasure mode with an ordinary
  update. A distinct kind is what lets the sequencer enforce T5's refusal at all.
- **Immutable settings, fixed at dataset creation.** Simplest, and it removes
  the fold. Rejected: turning erasure mode *on* for an existing dataset is a
  legitimate thing to want, and forbidding it would mean re-creating a dataset
  to gain the feature. Turning it *off* is the direction that needs refusing,
  and that is a narrower rule.

## Consequences

**A dataset describes itself.** Its behaviour is determined by its own bytes and
nothing else, which is what makes a copy a copy.

**Reading the settings means folding the log**, or reading them from the default
projection which already does. Cheap, and it inherits the archive horizon: a
fold from before an unattached archive fails explicitly (T3), like everything
else.

**Turning erasure mode on later does not retroactively make earlier terms
private.** The setting governs what happens from its position onward. A dataset
that may ever need erasure should be created with it on, and the limitation
belongs in the documentation rather than in a surprise.

**One more commit kind for every consumer to handle.** A projection, a
subscriber and a replica each now see three kinds where they saw two. The cost
is small and it is the cost of having configuration be first-class rather than
ambient.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0020, 0022,
  0023). Touches **0010** (I4's non-emptiness is a `Data` rule; `Settings`
  joins `Erasure` as an exemption), **0011** (T5 goes through the sequencer, so
  a settings change is serialised against the writes it governs — which is what
  makes "no private entries exist" a checkable condition rather than a race),
  **0014** (a `Settings` commit is in the header chain like any other), and
  **0023** (the default access scope is the second setting, and the first that
  is not a mode). No conflict with any.
- **Layer ownership.** The settings model, the `Settings` commit kind and T5's
  refusal are owned by `Varve.Store` at **layer 4**. Nothing about settings
  crosses a lower-layer contract.
- **Analyzer rule.** None.
- **Open questions owned.** None.
