# Decisions

Every Varve ADR as a **decision set**: the rulings code cites as types
([ADR 0062](../adr/0062-adopting-decisiondriven-analyzers.md)). The ADR in
[`docs/adr/`](../adr/) is the narrative and the reasoning. The set file is the
list of what was decided, one line each, in the form
[`DecisionDriven.Analyzers`](https://github.com/Hafeok/decision-driven-analyzers)
reads.

This is the package's **interim form**
([`docs/rules/ledger-input.md`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/ledger-input.md)):
markdown with YAML front matter, until the Decision Ledger can export. This
file has no front matter, so the generator skips it.

## One file per ADR

```yaml
---
set: enforcement-by-analyzers       # the ADR's file name without its number
namespace: varve                    # every Varve decision
adr: 0004                           # the ADR this set enumerates
decisions:
  - key: OffTheShelfFirst           # ^[A-Z][A-Za-z0-9]{0,63}$, unique across every file
    statement: "One line, double-quoted, no quote inside"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
---
```

- **A key is a ruling.** It identifies the decision across versions and
  becomes the type code cites: `[Contract(typeof(EnforcementByAnalyzers.OffTheShelfFirst))]`.
  It is never renamed, and never reused for something else.
- **Acceptance is transcribed from the ADR.** `accepted-at` is the ADR's date
  at `T00:00:00Z`. A ruling changed by a dated amendment inside its own ADR
  carries the amendment's date. A decision filed without acceptance compiles
  to `[Obsolete]`, so citing it is `CS0618`, and ADR
  [0066](../adr/0066-expected-red-pull-requests.md) says what a pull request
  carrying one is. **A session never writes `accepted-by`** for a decision it
  filed.
- **Supersession moves a ruling; it does not delete it.** A ruling a later ADR
  supersedes appears once, in the superseding ADR's set, under the same key and
  with that ADR's acceptance. The superseded ADR's set keeps only the rulings
  still in force. An ADR with none in force has no set file.
- **`revoked-at`** is for a ruling withdrawn with no successor. It compiles to
  an error, `CS0619`, on every citation. It should be rare, and every use is
  listed in the report of the change that makes it.

`dotnet run eng/decision-sets.cs` checks every file: key syntax, a key claimed
twice, missing fields, acceptance fields in pairs, the namespace, and that
`adr` names a real ADR. It runs in the build job and in `eng/ci.cs`.
