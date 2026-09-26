---
set: settings-commits-reach-every-subscriber
namespace: varve
adr: 0046
decisions:
  - key: ControlCommitsReachEverySubscriber
    statement: "Settings and Erasure commits are delivered to every subscriber regardless of filter, each as itself with its empty delta"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: DeterminismForCrashFreeHistories
    statement: "The determinism property holds for crash-free histories, because a tail recovery abandoned stays in the log"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: GraphScopeIsADeclaration
    statement: "The named-graph scope in a commit's metadata is a declaration the store records and never enforces, and validators may enforce it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: PrivateTermsHashByValue
    statement: "When private terms exist, a source's comparer hashes by value for every class of id"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
---

The rulings of [ADR 0046](../adr/0046-settings-commits-reach-every-subscriber.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

`ControlCommitsReachEverySubscriber` is ADR 0016's filter bypass for `Erasure` commits as this
ADR amended it to include `Settings` commits; it moved here from 0016's set.
