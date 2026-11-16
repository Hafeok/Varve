# Session, 2026-11-10: issues are the work log

Developed with AI assistance under human review. The full records, with the
prompts, are one per issue:
- [`2026-11-10-issue-257-issues-are-the-work-log.md`](../traceability/2026-11-10-issue-257-issues-are-the-work-log.md);
- [`2026-11-10-issue-209-closing-trailers-at-landing.md`](../traceability/2026-11-10-issue-209-closing-trailers-at-landing.md);
- [`2026-11-10-issue-248-the-descriptor-date-follows-the-digests.md`](../traceability/2026-11-10-issue-248-the-descriptor-date-follows-the-digests.md).

A cloud session, not the maintainer's delegate (ADR 0239 point 1). It filed
its decisions unaccepted, posted each head, and asked; it approved, landed
and accepted nothing.

## Pull requests

| PR | Step | Closes |
|---|---|---|
| [#259](https://github.com/Hafeok/Varve/pull/259) | 1: `AGENTS.md`'s *Issues are the work log*; ADR 0241, a commit message never names a model and its identifier is allowed | #257 (`Refs #169`) |
| [#260](https://github.com/Hafeok/Varve/pull/260), stacked on #259 | 2: `land.cs` checks closing trailers before a push and after `main` moves; the `Completes:` line | #209 |
| [#261](https://github.com/Hafeok/Varve/pull/261), stacked on #260 | 3: `release.cs --sbom` writes the digests and the date together; `land.cs` refuses a date over three days old | #248 |

They land in that order.

## ADRs

- **0241**, *A commit message never names a model; a model's identifier is
  allowed*, filed unaccepted. It supersedes one ruling of 0033,
  `ModelNamedInTheRecordNotTheCommit`, which moves to 0241's set under its
  key. A reversal is a supersession, not an amendment (ADR 0068 point 2), so
  the first plan, an amendment to 0033, was corrected on #257.
- **0102**, amended 2026-11-10, add-only, by #248: `date` is the day the
  digests were last written. No ruling changes. ADR 0224 is untouched.

## Issues

- **Opened:**
  - [#257](https://github.com/Hafeok/Varve/issues/257), the section, since no
    issue covered it;
  - [#258](https://github.com/Hafeok/Varve/issues/258), the decision report's
    baseline is stale on `main`.
- **Closed on landing:** #257, #209 and #248, each by a trailer line.
- **Touched in part:** #169. `AGENTS.md` states its ruling once #259 lands;
  its gate, rule page and port stay with it. The coordination is on #169.

## Test counts

| Suite | Before | After |
|---|---:|---:|
| `tests/fixtures/land/` (`land-fixtures`) | — | 12 cases |
| `tests/fixtures/release-sbom/` (`release-sbom-fixtures`) | — | 4 cases |
| `ci.yml`: a descriptor's digests rewritten on a later day | — | 1 step, Linux |

No .NET test project changed. The Release build has zero warnings.

## Questions

- **Accept ADR 0241's key** `ModelNamedInTheRecordNotTheCommit`. Nothing in
  code cites it, so #259 is not red on `CS0618`; it waits on acceptance all
  the same.
- **Every open pull request needs a `Completes:` line** before `land.cs` lands
  it, once #260 is on `main`. Today that is #253, #252, #247, #242, #238 and
  #236.
- **The records leave the model for the maintainer.** The tool's configuration
  keeps model identifiers out of every file it pushes, so ADR 0033's provision
  applies. The identifier is in the session's metadata.

## Deferred

- [#258](https://github.com/Hafeok/Varve/issues/258): regenerate the decision
  report's baseline in a change of its own.
