---
set: blank-nodes-at-the-protocol-boundary
namespace: varve
adr: 0098
decisions:
  - key: BlankLabelsOutAreStable
    statement: "A store blank node leaves the server as a blank node labelled from its store identity, the same label in every response and record of the dataset"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: BlankLabelsInAreFresh
    statement: "A blank node label in a request is scoped to that request and never addresses an existing node"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: NoSkolemisation
    statement: "Varve mints no skolem IRIs and gives /.well-known/genid/ no meaning; skolemisation on request is the recorded extension"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
---

The rulings of [ADR 0098](../adr/0098-blank-nodes-at-the-protocol-boundary.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
