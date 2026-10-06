---
set: the-identity-map
namespace: varve
adr: 0087
decisions:
  - key: IdentityMapNamesWhoActsForAnAgent
    statement: "eng/identities.json lists humans with their emails, login and delegates, agents with their responsible human, and exempt automation; it is the interim form of the ledger's authority model, replaced by the ledger's grants"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: DelegatesChangeOnlyInTheirHumansPullRequest
    statement: "A human's delegates change only in a pull request that human authored, judged against the base's map"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: SignOffByAuthorOrResponsibleHuman
    statement: "Every non-merge commit is signed off by its author by name and email, or, for an agent's commit, by its responsible human or a delegate; exempt automation is skipped"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: AgentDecisionsAcceptedByTheirHuman
    statement: "A decision first filed by an agent's commit is accepted only by that agent's responsible human or a delegate"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: AgentPullRequestsApprovedOnTheHead
    statement: "A pull request containing an agent's commits needs an approving review on its current head from the agent's responsible human or a delegate"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: OwnPullRequestApprovedByComment
    statement: "For a pull request opened under the responsible human's own login, a comment review by that human containing approve and the current head sha counts as their approval; an earlier head or another author counts for nothing"
  - key: ApprovalBecomesASignedLedgerReview
    statement: "When the ledger gates ship, an approval is a signed ledger Review over the head sha by an identity holding Approve, and the GitHub review and comment forms are retired with the identity map"
---

The rulings of [ADR 0087](../adr/0087-the-identity-map.md), written by the pre-release session of
#63 at the maintainer's decision, filed without `accepted-by` (ADR 0066).
