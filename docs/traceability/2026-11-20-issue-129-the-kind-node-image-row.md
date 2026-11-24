# The kind node image's row, ahead of the kind job

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#129](https://github.com/Hafeok/Varve/issues/129), part D's image (#361) |
| **Date** | 2026-11-20 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239) |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS9TK9P9VYitD1bP8Z` |
| **Branch** | `claude/kind-node-image-129`, from `main` at bd5eedd8 |
| **ADRs** | none new |

## The prompt

> D (#361):
> 1. Open a pull request that carries only the `kind-node` row in
>    `.github/ci-images.yaml` (and its register entry if one is owed), on
>    a branch of its own. Report its head as the full hash.
> 2. After it lands, the maintainer runs `mirror-ci-images.yml` on `main`
>    and sets the package private. Then merge `main` into #361, let the
>    `kind` job run, read it, and report. #361 is not approved before that
>    job has passed once.

## The report

- **The row** is #361's, unchanged: `gcr.io/k8s-staging-kind/node:v1.37.0` at `sha256:a1ed56cf…`, kind v0.33.0's default node image, copied from kind's staging registry because no row may name Docker Hub as its upstream.
- **What the row leaves out:** #361's other change to `ci-images.yaml`, adding `kubernetes (kind)` to the `azurite` row's jobs, stays in #361 with the job itself.
- **Its job, `kubernetes (kind)`, doesn't exist on `main` yet.** The `ci-images` gate checks a row's jobs only through its allowances, and this row has none, so the gate passes.
- **The register.** `Directory.Packages.props`'s image table calls itself the register of every dependency, so the image gets a line. `kubeconform`, a row since ADR 0181, has none; that is filed as its own issue rather than fixed here.
- **Checked:** `ci-images`, `ci-images-fixture` and `register` pass on `main` with this change.
