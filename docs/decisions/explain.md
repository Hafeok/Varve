---
set: explain
namespace: varve
adr: 0199
decisions:
  - key: ExplainRoute
    statement: "GET /datasets/{name}/explain?quad= answers a quad's status at the read's position, explicit, implicit with a proof, or absent, as JSON"
  - key: WellFoundedProof
    statement: "An implicit quad's proof names its rule and its premises, each explicit with its graph or implicit with a proof that never uses a quad on its path, found by backward search under the read's work limit"
  - key: ExplainUnderGrants
    statement: "Explain answers only for a quad the caller sees, so every premise is in a graph it reads or a TBox or rules graph, and an unseen quad is absent"
---

The rulings of [ADR 0199](../adr/0199-explain.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 2 (#171) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
