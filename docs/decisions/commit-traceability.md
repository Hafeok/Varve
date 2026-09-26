---
set: commit-traceability
namespace: varve
adr: 0033
decisions:
  - key: CommitsReferenceAnIssue
    statement: "Every commit on main carries Refs #N or Closes #N in its body, enforced by eng/issue-refs.cs"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: IssueBeforeWork
    statement: "An issue exists before the work it tracks"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: MergeCommitsExempt
    statement: "Merge commits are exempt from the issue reference, by parent count"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: BotCommitsExempt
    statement: "Commits by dependabot[bot] and github-actions[bot], a closed list in the gate's source, are exempt from the issue reference"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: TraceabilityRecordPerSession
    statement: "Every AI-assisted session files docs/traceability/YYYY-MM-DD-issue-N-slug.md with its prompt, the tool and model named exactly, its report and its issue"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: BackfillsSayWhatWasReconstructed
    statement: "A backfilled record says plainly what was reconstructed rather than recorded"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: TopicSummariesForOutsideWork
    statement: "A design conversation held outside the repository is recorded as a topic summary naming its decisions, and the maintainer holds the transcript"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: ProseNamesNoProduct
    statement: "Prose describes the project as developed with AI assistance under human review and names no product, while records and issues name the tool and model"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: AgentsMdHoldsTheRules
    statement: "The agent rules live in the vendor-neutral AGENTS.md, and CLAUDE.md is one line pointing at it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
---

The rulings of [ADR 0033](../adr/0033-commit-traceability.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
