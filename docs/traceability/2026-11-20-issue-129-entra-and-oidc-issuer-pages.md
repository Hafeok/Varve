# The Entra page and the issuer page, and Authenticate cut to its concepts

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#129](https://github.com/Hafeok/Varve/issues/129), pull request B2 of the split |
| **Date** | 2026-11-20 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239) |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS9TK9P9VYitD1bP8Z` |
| **Branch** | `claude/operator-guide-b2-129`, from `main` at bd5eedd8 |
| **ADRs** | none new |

## The prompt

> B2:
> 1. No CLI change. Write the Entra and issuer pages around the keyless
>    flow (`--client-assertion`, RFC 7523 §2.2).
> 2. The Zitadel seed stays on its secret. The page says so where the
>    reader meets it: why (no federation for machine users, with the
>    Zitadel pages you cited), that it is for local trial and the
>    `auth (zitadel)` job, and that it is not a production pattern.
> 3. Do not file the JWT-profile issue. If you think the page still
>    leaves a reader expecting more than is served, say where on #129.

## The report

- **Stopped first, under answer 7.** On 2026-11-19 the session stopped B2 before writing and reported on #129 that the flows don't meet:
  - the CLI's `--client-assertion` sends RFC 7523 §2.2;
  - Zitadel documents §2.1 for a machine user's key and `client_secret` for its client credentials;
  - Zitadel has no federation for machine users.
- **The ruling above is the answer.**
- **`docs/operator/entra.md`.** A procedure against `deploy/azure/entra/main.bicep` (#319, landed with B1), with a check after each step:
  - the registrations and their outputs;
  - the roles, and the server's settings;
  - `varve apply --client-assertion github` from a `varve-deploy` job, with the Kubernetes token-file and Container Apps managed-identity variants;
  - grants, and the Web UI and the command line as public clients;
  - on-behalf-of, moved here from `authenticate.md`.
  - It says the data-plane commands have no keyless sign-in until #301, and shows `VARVE_TOKEN` from the platform's token as the keyless way meanwhile.
  - Every block is marked as run by no job, for want of a tenant (ADR 0223), and its "Not run in CI" names the jobs that cover the nearest equivalent.
- **`docs/operator/oidc-issuer.md`.**
  - **What an issuer must provide:** moved from `authenticate.md`.
  - **A new section:** keyless automation needs the issuer to trust the platform's token as a client credential.
  - **Zitadel step by step**, each block marked with the `auth-zitadel` or `container` step it repeats.
  - **The seed's machine-user secret** is explained at step 2, where the reader meets it: why, with Zitadel's grant-types, endpoints and token-exchange pages; that it is for a local trial and `auth (zitadel)`; and that it is not a production pattern.
  - **Unchanged:** `eng/zitadel-seed.cs` and `auth (zitadel)` are not changed.
- **`authenticate.md`** keeps the model, adds a section pointing to both new pages, and keeps:
  - the attribution rule and refusals, each now a section of its own;
  - the management plane, anonymous mode and the command line.
  - The command line now names `--client-assertion` and `--managed-identity`, and where #301 leaves them missing.
  - Every anchor other pages link to is kept.
- **Links** moved from the old sections, in `deploy/azure/entra/README.md`, `azure-container-apps.md` and `web-ui.md`. The guide's index counts nineteen pages.
- **Checked:** the `procedures` gate passes (21 pages, 90 blocks, 0 findings), and so does `site-links`.
