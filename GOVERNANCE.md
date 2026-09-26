# Governance

Varve is run to the **Mind Over Machine open-source stewardship standard**
([mindovermachine-dev](https://github.com/mindovermachine-dev)), adopted in full
at [#16](https://github.com/Hafeok/Varve/issues/16). Moving between the
foundation's projects should feel familiar; where this document differs from
another of them, that is a defect unless it says why.

The organisation publishes no governance template, so this is written to the
standard's wording rather than mirrored from a file.

## Who decides

**Emil Okkels Klein** is the maintainer, sole copyright holder, and the only
person who can merge a pull request or approve a release.

That is the whole list, and it is worth stating plainly rather than
implying a committee. One maintainer is the reason several rules here look
stricter than a small project needs: with nobody to catch a mistake in review,
the gates have to catch it instead.

## How decisions are recorded

**An ADR, or it did not happen.** `docs/adr/` holds every architectural
decision, numbered, each with **Alternatives considered** — an ADR that lists
no losing option has recorded an outcome, not a decision, and the next person
cannot tell whether the alternative was rejected or never seen.

**An accepted ADR is never edited.** A decision that still stands but needs
detail gets a **dated amendment** inside it (add-only and dated, [ADR 0068](docs/adr/0068-dated-amendments.md)); a decision that changed gets a
**superseding ADR**, and the old one keeps its text so the reasoning that was
wrong stays readable. Four ADRs in the stewardship set exist because this rule
forbids the easier alternative of quietly rewriting the old ones.

**Authority, in order**: `docs/brief.md`, then
`docs/spec/log-and-projection-model.md` for `Varve.Store` behaviour, then the
accepted ADRs, then everything else. Where an ADR departs from the brief — as
[0031](docs/adr/0031-licence-mpl-2-0.md) does on the licence — the departure is
recorded in the ADR and the brief stays unedited.

An ADR may be **proposed by anyone**, as an issue using the ADR-proposal
template. The maintainer accepts or rejects it, and a rejection says why in
writing.

## How work reaches the trunk

**`main` is the trunk. Pull requests are optional.**
([ADR 0032](docs/adr/0032-trunk-based-development.md))

Anyone with write access commits to `main` directly or through a pull request,
their choice. Neither is the lesser form of the other. A change not ready for
the trunk lives behind a feature flag or stays local — **not on a long-lived
branch**.

**The blocking review is the automated one.** Every gate runs in CI on every
push to `main` and every pull request, and a red trunk is fixed forward before
anything else lands. The gates are not required checks on the `trunk` ruleset:
GitHub would then refuse every direct push, because a pushed commit has not been
built yet ([ADR 0032](docs/adr/0032-trunk-based-development.md), amendment of
2026-09-24). A human would not catch what the ratchet catches.

**Human review is required for a release, not for a merge.** `publish.yml` runs
in the `release` environment, which has the maintainer as a required reviewer.
Publishing stops and waits for a person, because a version pushed to nuget.org
cannot be edited, replaced or deleted.

Pull requests stay welcome, and are the right tool for a change that wants
discussion or comes from outside. Reviews on them are **non-blocking**: an
unreviewed pull request whose checks are green is not waiting for anybody.

## Repository settings

The settings are declared in [`.github/repo-standard.yaml`](.github/repo-standard.yaml),
which is the source of truth: a push that changes it applies it, and a weekly
check opens a "Configuration drift" issue when the repository and the file
disagree ([ADR 0039](docs/adr/0039-repo-standard.md)). This table summarises the
file and follows it.

| | What |
|---|---|
| **`trunk`** ruleset on `main` | no deletion, no force push, **no bypass**; no required status checks (see above) |
| **`Signed Commits`** ruleset on `main` | required signed commits, with the admin role and the Claude GitHub App on the **bypass list** ([ADR 0034](docs/adr/0034-commit-signing-and-the-sandbox-exception.md)) |
| **`Varve Release Approval`** ruleset on `v*` tags | creating, deleting or force-moving a `v*` tag is restricted to the maintainer |
| **Environment** `release` | maintainer as required reviewer; referenced by `publish.yml` |
| **Discussions** | on; the categories are not yet declared (GitHub creates its six defaults, and no API can remove one) |
| **Projects** | *Varve roadmap* (upstream) and *Varve work* (downstream) |

### The signing exception

Commits from AI sessions running in the cloud sandbox are **exempt from the
signature requirement**, through ruleset 2's bypass. **This is a stated
deviation from the standard**, and the reason is not the obvious one.

The sandbox *does* sign — every commit carries an SSH signature — but GitHub
reports `unknown_key`, because the key is provided by the sandbox platform and
is registered to nobody. Registering it was considered and **rejected**: the key
is not ours, cannot be rotated by us, and is reused across sessions, so
registering it would let any such session produce commits GitHub attests as the
maintainer's.

What attests those commits instead: **the push path**, since only the
maintainer's installation can push here, and **the traceability record** in
`docs/traceability/`, which says what was asked for and what came back. A local
session with a key signs like a human. Sign-off is required from every session
regardless.

## The two boards

The standard asks for an **upstream Kanban board representing the roadmap** and
**downstream boards designed to minimise work in progress**. Varve has one of
each.

**Varve roadmap** — the upstream. One card per milestone, in order, mirroring
`docs/roadmap.md`. It answers "what is this project going to be?" and it moves
slowly. Cards are the milestone issues, [#4](https://github.com/Hafeok/Varve/issues/4)
to [#15](https://github.com/Hafeok/Varve/issues/15).

**Varve work** — the downstream. A Kanban with a **work-in-progress limit of 3**
in the *In progress* column. It answers "what is happening now?"

```
Varve roadmap  ──►  Varve work  ──►  done
  (milestones)       Todo · In progress (max 3) · In review · Done
```

**An issue enters the work board only when it is next.** Not when it is
interesting, not when it is filed, and not because a milestone has started. The
roadmap board is allowed to be long; the work board is not, and the limit of 3
is the mechanism that keeps it honest. An issue that cannot progress goes back
rather than sitting in *In progress* as a lie about what is being worked on.

### Issue assignment

- **An issue is assigned before work starts.** An unassigned issue in progress
  is an invitation for two people to do it twice.
- **An issue idle for 14 days is unassigned**, with a comment. This is not a
  reprimand: it makes the board describe what is happening, and picking the
  issue back up is one click.
- Every commit references an issue, enforced
  ([ADR 0033](docs/adr/0033-commit-traceability.md)), so the board and the
  history cannot drift apart.

## Channels

| For | Where |
|---|---|
| Questions, ideas, showing what you built | [Discussions](https://github.com/Hafeok/Varve/discussions) — Q&A, Ideas, Show and tell |
| Release notes and anything you should know | Discussions — Announcements |
| Anything actionable: a bug, a spec deviation, an ADR proposal, a feature | [Issues](https://github.com/Hafeok/Varve/issues) |
| A vulnerability | **Privately.** [SECURITY.md](SECURITY.md) |
| Conduct | [emil@okkels-klein.dk](mailto:emil@okkels-klein.dk), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) |

Blank issues are disabled. The templates exist because a bug report without the
document that reproduces it costs more to triage than to write.

## Becoming a maintainer

There is one maintainer and no process theatre to dress that up. What would
actually lead to a second:

1. **A track record in this repository** — several merged changes, including at
   least one that touched a decision rather than only code: an ADR, a gate, or a
   specification.
2. **Judgement about the rules**, shown by using them. Noticing that a change
   needs a superseding ADR, or that a new rule needs an analyzer before it needs
   a document, is the thing being looked for.
3. **An invitation from the maintainer**, recorded as an issue and announced in
   Discussions.

A new maintainer gets write access, merge rights, and a say in ADRs. Release
approval and the rulesets stay with the copyright holder until there is a
reason to move them — and moving them is itself an ADR.

**A contribution changes what a licence change costs.** Varve could be
relicensed in 2026 because one person held the copyright
([ADR 0031](docs/adr/0031-licence-mpl-2-0.md)). After the first outside
contribution that stops being true: a licence change then needs every
contributor's consent. There is **no CLA and no copyright assignment** — the
foundation's position is that software is not a company's intellectual property
— so the practical consequence is simply that the licence is now settled.

## AI assistance

Varve is developed with AI assistance under human review. Sessions are held to
the same standards as anyone else: the same gates, the same ratchet, the same
requirement that a decision be an ADR.

Every AI-assisted session leaves a record in `docs/traceability/` naming the
prompt, the tool, the model and the report
([ADR 0033](docs/adr/0033-commit-traceability.md)). `AGENTS.md` is the
instruction file a session reads.

The division is not subtle: **a session produces work, a person is accountable
for it.** Every commit carries a DCO sign-off naming that person, including
every commit from a session, and that is exactly what the sign-off is for.
