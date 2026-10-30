---
set: the-row-level-write-rule
namespace: varve
adr: 0236
decisions:
  - key: RowRestrictedWriterWritesOnlyItsRows
    statement: "A row-restricted writer's quads in a classified graph have a subject whose subjects are non-empty and within its writable set before and after, or no row before and after respectively"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: CreationAtFreshBlankOrCreateUnder
    statement: "A subject without a row may be created by a row-restricted writer only as a fresh blank node of the request or an IRI under its createUnder prefixes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ObjectsLinkToReadableRows
    statement: "An added quad under a non-public predicate whose object is an IRI or a not-fresh blank node needs the object to have a row the writer reads, and the quad's about-set within its writable set"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: RetractionsReadTheVisibleView
    statement: "A retraction of a quad the writer does not see is a no-op, and every pattern evaluates over the writer's row-scoped source"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: RequestedAndEffectiveBothChecked
    statement: "The write rule is checked on the requested quads at the pinned position and again on the effective delta by a validator of the request under the sequencer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: OneRefusalForEveryReason
    statement: "Every breach is 403 row-not-writable with one body naming no node, subject or binding, and nothing commits"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
---

The rulings of [ADR 0236](../adr/0236-the-row-level-write-rule.md), filed unaccepted by milestone 9, part 1 ([#202](https://github.com/Hafeok/Varve/issues/202), under #14)
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
