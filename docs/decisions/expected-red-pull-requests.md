---
set: expected-red-pull-requests
namespace: varve
adr: 0066
decisions:
  - key: ExpectedRedOnlyOnCs0618
    statement: "A pull request may arrive red only when every failure is CS0618 from citing a decision filed without acceptance, and its body lists those decisions"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: MaintainerAcceptanceIsTheReview
    statement: "The maintainer accepts a filed decision by adding accepted-by and accepted-at on the pull request's branch in a signed commit of their own"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: MergedOnlyGreen
    statement: "An expected-red pull request is merged only green"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: UnacceptedCitationsOnlyByPullRequest
    statement: "A change citing an unaccepted decision reaches main only through a pull request, never by a direct push"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: NoCommittedCs0618Downgrade
    statement: "No committed configuration downgrades CS0618"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: LocalOverrideIsNotConfiguration
    statement: "Verifying a branch with a command-line override for CS0618 is allowed and is not configuration"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: SessionsNeverWriteAcceptedBy
    statement: "A session never writes accepted-by for a decision it filed, and transcribes only acceptances an ADR already records"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0066](../adr/0066-expected-red-pull-requests.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
