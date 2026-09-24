# The trunk ruleset's required checks, decided rather than assumed

| | |
|---|---|
| **Issue** | [#27](https://github.com/Hafeok/Varve/issues/27) |
| **Date** | 2026-09-24 |
| **Tool** | Claude Code (cloud session, `claude.ai/code`) |
| **Model** | Claude Opus 5.5 (`claude-opus-5-5`) |
| **Branch** | `claude/epic-feynman-6deidm` |
| **ADR** | [0032](../adr/0032-trunk-based-development.md), amended |

Contemporaneous. Follows [#23](2026-09-23-issue-23-repo-standard.md), whose
first export of the repository's settings showed the `trunk` ruleset requiring
no status checks.

## The prompt

> add the CI jobs as required checks on trunk

## What happened

The session did not add them straight away. Requiring the checks would have
contradicted an accepted ADR, and the repository's rule is to say so rather than
diverge quietly. GitHub checks a required status against the pushed commit, and
a commit pushed straight to `main` has not been built. With no bypass on
`trunk`, every direct push would be refused, which ends ADR 0032's decision 1:
"commit to `main` directly or through a pull request, their choice".

The maintainer was asked to choose between three options: pull requests only
(superseding 0032's choice of route), required checks with an admin bypass
(superseding 0032's "no bypass list"), or leaving the checks off. They also chose
whether to keep the ruleset's strict mode, which requires a branch to be up to
date with `main` before it merges.

**Decided:** leave the checks off, and turn strict mode off.

## What changed

- `.github/repo-standard.yaml`: `strict_required_status_checks_policy: false`
  on `trunk`. The push to `main` applies it through the repo-standard workflow.
- ADR 0032: a dated amendment. The list of required checks is empty and stays
  empty; the gate is CI after the push, and a red trunk is fixed forward. It
  also records the condition for revisiting.
- `GOVERNANCE.md`:
  - The "blocking review" paragraph now says the same as the amendment.
  - The repository-settings table now follows the declaration, which it names as
    the source of truth. Three rows were wrong against the export: the tag
    ruleset has no signed-tags rule, ruleset 2's bypass list also names the
    admin role, and Discussions is off.

## Left for the maintainer

`GOVERNANCE.md`'s Channels table still sends questions, ideas and announcements
to Discussions, which is off. Either Discussions is turned on (the declaration
then needs `features.discussions: true`) or the table changes. That is a
decision about the project's channels, not a correction, so it was left alone.
