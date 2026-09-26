---
set: commit-signing-and-the-sandbox-exception
namespace: varve
adr: 0034
decisions:
  - key: HumanCommitsSigned
    statement: "Commits on main by human committers are signed, with GPG or SSH"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: HistoryNotRewrittenForSignatures
    statement: "Existing unsigned history is not rewritten to add signatures"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: DcoSignOffOnEveryCommit
    statement: "Every commit, human or AI session, carries a DCO Signed-off-by naming a person who may contribute the code"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: SandboxSignatureException
    statement: "Commits from AI sessions in the cloud sandbox are exempt from the signature requirement through the pushing App's bypass, a stated deviation from the standard"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: PushPathAndRecordAttest
    statement: "A sandbox commit is attested by the push path and its traceability record instead of a signature"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: LocalSessionSignsLikeAHuman
    statement: "A session running locally with the maintainer's key signs like a human and gets no bypass"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: SandboxKeyNeverRegistered
    statement: "The sandbox platform's signing key is never registered as a signing key on the maintainer's account"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: BranchBoundSessionLandsByPullRequest
    statement: "A cloud session bound to a development branch lands its work through a pull request the maintainer merges"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
---

The rulings of [ADR 0034](../adr/0034-commit-signing-and-the-sandbox-exception.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

`SandboxKeyNeverRegistered` is enumerated from *Alternatives considered*, the one rejection the
ADR itself calls "the important rejection" and states as a prohibition in its Context. The
revisit condition is not a ruling. ADR 0066's amendment is in 0066's set.
