# The repository About is declared

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#235](https://github.com/Hafeok/Varve/issues/235) |
| **Date** | 2026-11-06 |
| **Tool** | Claude Code, a cloud session started from the claude.ai app |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYQD2ZzENqUKdpeSgf4` |
| **Branch** | `repo-about`, from `main` at 5638f01 |
| **ADRs** | [0039](../adr/0039-repo-standard.md), applied, not changed |

## The prompt

> in the repo-standard we need a desciption, website and topics to enable others to quickly see what the repo is about.

## What was found

The repository's About panel was empty: the REST API returned a `null`
description, a `null` homepage and no topics. The declaration, as written by the
first `repo-standard export` (38fe4c05), declares `description: ""`,
`homepage: ""` and `topics: []`. A declared empty value is managed
(`tools/repo-standard/docs/declaration.md` §1.1), so each `apply` on a push to
`main` cleared the values the maintainer had set by hand on 27 October.

## What changed

`.github/repo-standard.yaml`, the `repository` kind only:

- `description`: the paragraph drafted for the maintainer on 27 October and set
  by hand that day, 280 characters against GitHub's 350.
- `homepage`: `https://www.nuget.org/packages/Varve.Store`. The project's own
  namespace, `https://w3id.org/varve/`, returns 404 today; the homepage moves
  there once the redirect is registered.
- `topics`: the same 19 as on 27 October, as a set. `oxigraph` is left out on
  purpose: Varve is not affiliated with Oxigraph, and a topic reads as if it
  were.

The baseline in `tools/repo-standard/baselines/mom.yaml` is not changed: the
About is a repository's own content, not policy.

## Verification

- The declaration loads: `repo-standard plan` built from `tools/repo-standard`
  passed validation and reached the API.
- The plan itself could not complete from the session. The session's GitHub
  proxy refuses `GET /repos/{owner}/{repo}/vulnerability-alerts`, which the
  `repository` kind reads. The `Repository settings` workflow applies the
  declaration on the push to `main`, and its weekly `check` confirms it.

## Report

The About is set by `apply` once this lands. No other kind in the declaration
changes.
