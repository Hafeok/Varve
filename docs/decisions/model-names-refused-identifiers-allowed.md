---
set: model-names-refused-identifiers-allowed
namespace: varve
adr: 0241
decisions:
  - key: ModelNamedInTheRecordNotTheCommit
    statement: "The traceability record names the model by the identifier the session's metadata reports, filled in by the maintainer where the tool will not write it, and a commit message never carries a model's name, while a model's API identifier and the bare product name are allowed"
---

The ruling of [ADR 0241](../adr/0241-model-names-refused-identifiers-allowed.md),
filed unaccepted by the session for #257 (ADR 0066). It supersedes the ruling of
ADR 0033's amendment of 2026-10-02 under the same key, which moved here from
`commit-traceability` (ADR 0068 point 4).
