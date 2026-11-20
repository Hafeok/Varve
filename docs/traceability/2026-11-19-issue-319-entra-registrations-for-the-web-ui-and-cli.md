# The Entra registrations for the Web UI, the command line and CI's provider leg

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#319](https://github.com/Hafeok/Varve/issues/319); [#129](https://github.com/Hafeok/Varve/issues/129), pull request B1 of the split |
| **Date** | 2026-11-19 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239) |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS9TK9P9VYitD1bP8Z` |
| **Branch** | `claude/operator-guide-b1-129`, from `main` |
| **ADRs** | none new |

## The prompt

> 1. Branches: push each remaining part to its own branch, one pull request each: claude/operator-guide-b1-129, claude/operator-guide-b2-129, claude/operator-guide-c-129, claude/operator-guide-d-129. Base each on main. If a part depends on #345's changes, stack it on #345's branch and say so in its description. Try each push once; if one is refused, say so on #129 with the exact refusal and do not try another route.
> […]
> 4. Each new pull request: Completes its own issue or Completes: none with Refs #129, its decisions filed unaccepted, and its ADR status line in the "Accepted — filed unaccepted by …" form.

The maintainer's answer 6 of 2026-11-16 (on #129): "The Entra Bicep additions are a separate issue and pull request, landed before the Entra page."

## The report

- **What `deploy/azure/entra/main.bicep` adds**, as #319 asks:
  - **the Web UI**, a SPA registration: authorisation code with PKCE, no secret, redirect URIs from `webUiRedirectUris` (default `http://localhost:8080/ui/`), with both planes' `access_as_user` and admin consent for each;
  - **the command line**, a public client (`isFallbackPublicClient`) for device code, with the same two scopes and consents;
  - **a delegated scope `access_as_user` on `api://varve-manage`**, with a stable id;
  - **CI's provider leg** (ADR 0223), `Varve.Write` as an application permission, its only credential the federated credential for `repo:<repository>:environment:auth-providers`;
  - **outputs** `webUiClientId`, `cliClientId`, `dataScope`, `manageScope` and `providerLegClientId`.
- **No secret or certificate** is created anywhere.
- **Checked:** `bicep build` and `bicep lint` on the template, as the `bicep` job runs them. Not deployed: the session has no tenant.
- **Docs:** `deploy/azure/entra/README.md` names the new registrations and outputs; `docs/auth-providers.md` says the template's provider-leg client replaces its manual steps 1 to 3. The Entra page itself is B2's.
