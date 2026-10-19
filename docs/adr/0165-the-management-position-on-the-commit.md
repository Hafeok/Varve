# 0165 — The management position on the commit: a host attachment, no format change

## Status

**Accepted — filed unaccepted by milestone Management plane of #105,
2026-10-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. It answers question 1 of the plan of #105: format v1 carries the
position, and no `derived/` audit index is needed.

**Specification 1.8.** `docs/spec/log-and-projection-model.md` §1's `meta` and
T1 step 5 are widened, as below. **No byte of storage format v1 changes.**

## Context

ADR 0163 point 3 has a commit record the management position its write was
authorised under. Two questions then become exact queries:
- "Was this write allowed, and by which grant?"
- From part 2 on: which event-schema declarations render this commit?

Storage format v1's commit header (§4.5) is fixed-width, and the reader
refuses unknown bytes. Its one counted, open list is the attachments: term ids
any reader accepts, covered by the header hash and delivered by the change
feed (spec §2.1 of the change feed). Until now only a validator could add one
(ADR 0017, ADR 0058).

ADR 0127 kept the application's `azp` out of the log: "the log records who,
not through what". A management position is not "through what". It names the
rules under which this write was allowed, which is the same kind of fact as a
validation report.

## Decision

1. **The term.** The position is the typed literal
   `"<p>"^^<https://w3id.org/varve/ns#managementPosition>`, in canonical
   `xsd:integer` lexical form. That makes one dictionary term per distinct
   management position, which is bounded by the system dataset's commits.
2. **The host attaches it.** `CommitMetadata.Attachments` takes terms the host
   attaches to the commit. The sequencer resolves them as it resolves the
   agent and the cause, and records them before the validators' attachments.
3. **The specification (1.8).**
   - §1's `meta` attachments are "a validator's report, or metadata the host
     attaches".
   - T1 step 5 is unchanged for validators.
   - The datatype `varve:managementPosition` is reserved for the management
     position.
4. **Who attaches it.** Every `Data` and `Settings` commit made through a
   server of a fleet carries one. An embedded commit carries none: it has no
   management plane (ADR 0163 point 9).
5. **The positions never decrease along a log.**
   - A dataset's writer refuses a write whose applied management position is
     below the highest one its log already records.
   - Before refusing, it advances the system follower once.
   - Only then does it answer `503 management-behind`.

   So the recorded positions never decrease along each dataset's log. A
   property over generated histories with writer changes holds it.
6. **No audit index.** The commits resource already serves each commit's
   attachments, so "was this write allowed" needs no `derived/` index. It is
   an exact query: read the commit's attachment, then evaluate the grants as
   of that position (`/manage/datasets/{name}/grants` with `Varve-As-Of`).

## Alternatives considered

- **A `derived/` audit index.** It is not needed: the header already carries
  the value, and a derived index would be one more thing to rebuild.
- **Bytes in a `Data` commit's kind payload.** §4.6 says the payload is empty.
  Today's reader does not check that, so bytes put there would be silently
  ignored, and putting them there would be a format change in disguise.
- **Quads in a system graph of the data dataset.** I2 removes a repeated
  value unless every record has its own subject. The graph would grow by one
  node per commit, and every write would touch it, which interacts with ADR
  0125's scopes and ADR 0107's grants.
- **The cause or the scope field.** They have their meanings (ADRs 0094,
  0125).

## Consequences

- **The commits resource and the change feed** show the attachment with no
  protocol change. The feed specification's comment "what validators
  attached" becomes "what validators and the host attached".
- **`Varve.Store`'s baseline** gains `CommitMetadata.Attachments`.
- **A reader of an older version** reads the attachment as an opaque term, as
  it reads any.

## Checks

- **Checked against the accepted ADRs** (0001–0147) and specification 1.7.
  It touches:
  - **0017** and **0058** (attachments; a dated block in each);
  - **0072** (no byte changes);
  - **0094** (the cause untouched);
  - **0127** (distinguished above, not superseded).

  No conflict.
- **Layer ownership.** `Varve.Store` (4) records it; `Varve.Server` (6)
  attaches it.
- **Analyzer rule.** None.
- **Open questions owned.** None.
