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
    statement: "A pull request containing an agent's commits needs, for every agent in it, an approval on its current head by a holder of Approve for that agent, its responsible human or a delegate"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
  - key: OwnPullRequestApprovedByComment
    statement: "For a pull request opened under the responsible human's own login, a comment review by that human containing approve and the current head sha counts as their approval; an earlier head or another author counts for nothing"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
    revoked-at: 2026-10-06T12:30:00Z
  - key: ApprovalBecomesASignedLedgerReview
    statement: "When the ledger gates ship, an approval is a signed ledger Review over the head sha by an identity holding Approve, and the GitHub review and comment forms are retired with the identity map"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T00:00:00Z
  - key: JudgingScriptIsMains
    statement: "Agent review is judged by main's eng/agent-review.cs, on triggers whose workflow is main's, reading the pull request through the API as data; nothing from the pull request is checked out, built or run"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
  - key: OnlyAppPinnedChecksCount
    statement: "A required check is matched by name and so is spoofable by any workflow a pull request adds; it holds against a pull request only when pinned to an App whose key no such workflow can reach, and agent review is pinned to the gates App"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
  - key: ApprovalIsAConversationComment
    statement: "The approval is a pull request conversation comment containing approve and at least twelve characters of the current head, by a holder of Approve for every agent in the pull request; review submissions are not consulted"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
  - key: CommentApprovalIsNotAValidWorkflow
    statement: "Approval by pull request comment is not a valid workflow: it is tolerated only until the ledger's review gate exists, and the pull request that adopts that gate removes it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
  - key: ApprovedHeadLandsThroughLand
    statement: "On a push to land/, an agent's commits are admitted when the pushed head is a pull request's head carrying the gates App's successful agent review check run; any other sha is not"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
---

The rulings of [ADR 0087](../adr/0087-the-identity-map.md), written by the pre-release session of
#63 at the maintainer's decision, filed without `accepted-by` (ADR 0066).

The second amendment of 2026-10-06 revokes `OwnPullRequestApprovedByComment`: its
review-comment form and its own-pull-request restriction are withdrawn whole, and the
conversation-comment form (`ApprovalIsAConversationComment`) is a different ruling, not
the same one restated. `AgentPullRequestsApprovedOnTheHead` keeps its key with its
statement changed by that amendment, so its acceptance is withdrawn until the
maintainer gives it again. `CommentApprovalIsNotAValidWorkflow` is the ruling the
ledger project cites for why the comment form exists at all.

`ApprovedHeadLandsThroughLand` is the amendment of 2026-10-08, filed without `accepted-by` by the
release-descriptor session of #73 (ADR 0066), so that an approved head can be fast-forwarded to
`main` and a release cut at it (ADR 0102).
