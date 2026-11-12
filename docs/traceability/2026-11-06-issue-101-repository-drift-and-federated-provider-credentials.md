# Repository drift, and the provider legs by federated credentials

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#101](https://github.com/Hafeok/Varve/issues/101); opened [#239](https://github.com/Hafeok/Varve/issues/239) and [#241](https://github.com/Hafeok/Varve/issues/241) |
| **Pull requests** | [#238](https://github.com/Hafeok/Varve/pull/238) (step 1, from `claude/gifted-gauss-xyxhld`); [#242](https://github.com/Hafeok/Varve/pull/242) (step 2, from `claude/gifted-gauss-xyxhld-step2`, stacked on #238, a second branch by the maintainer's leave) |
| **Date** | 2026-11-06 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and last served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRvXwQujZvssWPK7ST` |
| **Branch** | `claude/gifted-gauss-xyxhld`, from `main` at 5638f01 |
| **ADRs** | [0223](../adr/0223-provider-legs-by-federated-credentials.md) (filed unaccepted); [0100](../adr/0100-authentication-tested-in-three-layers.md) (superseded in part, point 4) |
| **Session summary** | [`docs/sessions/2026-11-repository-drift-and-federated-credentials.md`](../sessions/2026-11-repository-drift-and-federated-credentials.md) |

## The prompt

> Session prompt: repository drift (#101) and federated provider credentials
> Paste into a fresh session on `Hafeok/varve`. Two steps, each one green PR, in this order. Read `AGENTS.md` first and follow it, including the issue-log practice: comment on each issue when you start it, post your plan there, and after landing check that the issues are closed.
> The diagnosis is on #101 (comment of 2026-11-06). The maintainer's rulings, which are closed:
>
> * The undeclared labels and environment are declared, not deleted.
> * The provider legs of ADR 0100 move to federated credentials from GitHub's OIDC token. No client secret and no service-account key, here or anywhere.
> * The Entra and Google test tenants aren't set up yet. The legs must skip cleanly until they are, and the setup is documented for the maintainer to do later.
>
> Step 1: the declaration catches up (#101)
> `.github/repo-standard.yaml` describes the repository as it is:
>
> * Labels: declare `adr`, `soak` and `spike`. Take each one's colour and description from the live label, so `apply` changes nothing. `soak` is load-bearing: the soak judge opens issues with it (ADR 0215).
> * Environment `azure-objects`: declare it as it exists, covering its branch policy and the secrets `azure-objects.yml` and `azure-objects-faults.yml` read. If its live branch policy allows anything other than `main`, don't declare the looser policy. Restrict it to `main` (the rule: "Secrets only in environments restricted to `main`") and say so on #101.
> * Ruleset `land`: the only difference is `update`'s `update_allows_fetch_and_merge: false`. Find out which side is right: what ADR 0088 and the `land/` flow need, and what GitHub returns as the default. Make the declaration match. If it's a normalisation difference in repo-standard's comparison (the parameter at its default reads as a difference), fix the comparison in `tools/repo-standard` instead, with a test, and say which it was.
> * `auth-providers`: leave it as it is in this step. Step 2 changes it.
>
> Gate: `repo-standard check` against the live repository reports only the five `auth-providers` secrets as "declared and not present". The push run of Repository settings on `main` is green or fails only on those. Update `tests/fixtures/required-checks/repo-standard.yaml` if it covers any of this.
> Commit trailer: `Refs #101`. Close #101 in step 2, when the declaration fully converges.
> Step 2: the provider legs use federated credentials
> The decision first. A new ADR amends ADR 0100's point 4 and does not edit it, because ADR 0100 is accepted. Its status line reads "Accepted — filed unaccepted by …". The decision is:
>
> * Entra: client credentials with a federated identity credential.
>    * The job requests GitHub's OIDC token with audience `api://AzureADTokenExchange`, using `id-token: write` on that job only.
>    * The test sends that token as `client_assertion`, with `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer`, to the v2 token endpoint.
>    * The app registration trusts the subject `repo:Hafeok/Varve:environment:auth-providers`.
>    * What's asserted is unchanged: `oid` as the subject, and the app role in `roles`.
> * Google: Workload Identity Federation.
>    * The GitHub OIDC token is exchanged at Google STS for a federated access token.
>    * That token calls IAM Credentials `generateIdToken` for the service account, with the audience of our choosing.
>    * The pool's provider is restricted by an attribute condition to this repository and to the `auth-providers` environment.
>    * What's asserted is unchanged: the permission is granted to the `email` claim.
> * No third-party action does either exchange: no `azure/login`, no `google-github-actions/auth`. The exchanges are a few HTTP calls in the test (or a small `eng/` script). Constraint 4 is the reason, and the calls are short.
> * The skip:
>    * The legs skip unless their configuration is present.
>    * The configuration is identifiers, not credentials: tenant id, client id, audience, the workload identity provider's resource name, and the service account's email.
>    * A configured leg that then fails is a failure. It is never turned into a skip.
>    * `auth (providers)` stays a non-required job on pushes to `main`, as ADR 0100 has it.
> * Alternatives the ADR records as rejected:
>    * a client secret or service-account key in the environment: a long-lived credential, against the standing rule and ADR 0037's spirit;
>    * certificate credentials: still a long-lived private key to store and rotate;
>    * the vendors' login actions: third-party dependencies for two HTTP calls.
>
> Then the change:
>
> * `tests/Varve.Server.Tests/ProviderTests.cs` and the `auth (providers)` job in `ci.yml` do the exchanges above. The skip message names what is missing.
> * `.github/repo-standard.yaml`: `auth-providers` no longer declares `VARVE_ENTRA_CLIENT_SECRET` or `VARVE_GOOGLE_SERVICE_ACCOUNT`. The identifiers are declared the way repo-standard supports. If it supports environment variables, use those, since they aren't secrets. If it doesn't, keep them as environment secrets, and open an issue for environment-variable support in `tools/repo-standard`, linked from #101. Don't build that support in this step.
> * Run `repo-standard check` after the change. The only remaining differences should be the identifiers not yet present, reported as "declared and not present". That is the expected state until the maintainer sets up the tenants. Record it on #101 and close #101 with a comment naming the commit, because the declaration is now right. The missing values are setup, not drift.
> * `docs/auth-providers.md`, for the maintainer to follow later, step by step:
>    * Entra: the app registration with v2 tokens and the app role; the federated credential's issuer `https://token.actions.githubusercontent.com`, subject and audience; the values to put in the environment.
>    * Google: the workload identity pool and OIDC provider, the attribute mapping and the condition, the service account and the `roles/iam.serviceAccountOpenIdTokenCreator` binding for the pool's principal, and the values to put in the environment.
>    * How to see the legs run, and what a correct first run reports.
> * Grep for any other use of the two removed secret names (traceability records excepted) and change them.
>
> Trailers: `Closes #101` on the declaration commit, and a `Refs` to the ADR's issue if you open one for it.
> Close-out
> `docs/sessions/2026-11-<name>.md` and a `docs/traceability/` record: PRs, the ADR number taken, the issues opened and closed, test counts before and after, questions, and anything deferred, each deferred item as a linked issue.

## The report

**Step 1, the declaration catches up** ([#238](https://github.com/Hafeok/Varve/pull/238), two commits, `Refs #101`).

- **Labels.** `adr` (`ededed`, no description), `soak` (`b60205`, ADR 0215's
  description) and `spike` (`ededed`, no description), read from the live
  labels by the API.
- **`azure-objects`.** Both workflows read only `vars.*` identifiers, so it is
  declared with `secrets: []`, and restricted to `main` with a custom policy.
  The live policy couldn't be read from this session. The evidence is that
  the only push run that named the environment, on `land/pr-154` (run
  40474078488), deployed to it with every variable empty. That matches the
  unprotected environment GitHub creates when a workflow first names one. So
  the `apply` on landing restricts it, as ADR 0142 and `docs/testing.md`
  already describe it. Said on #101.
- **`land`: a normalisation difference in repo-standard, not drift.** GitHub's
  REST `GET` returned the `update` rule bare, `{"type":"update"}`, while the
  declaration, in the export's spelling, has `update_allows_fetch_and_merge:
  false`. That is the parameter's default, and the value the `land/` flow
  needs (ADR 0088): only the maintainer's push moves a `land/` branch.
  `RulesetsResource` now removes a rule's parameters when they equal GitHub's
  defaults for its type (`update` is the one entry), on both sides. A
  recorded scenario, `rulesets-check-defaults`, covers both spellings either
  way round, plus `true`, which is still reported. It fails without the
  change.
- **The required-checks fixture** covers only ruleset 1's checks, so it is
  unchanged.

**Step 2, the provider legs by federated credentials** ([#242](https://github.com/Hafeok/Varve/pull/242),
`Closes #101` on the declaration commit).

- **ADR 0223**, filed unaccepted. 0223 was the one number no milestone
  claims: readiness A holds 0213–0222, readiness B starts at 0224, and
  milestone 9 starts at 0230. A change of meaning is a supersession under ADR
  0068 point 2. So 0223 *supersedes ADR 0100 in part* (point 4): 0100's body
  is untouched, its Status line names the supersession (0068 point 3), and
  `EntraAndGoogleBySecret` moves to 0223's set under its key, restated. Five
  new rulings join it, all unaccepted, none cited in code.
- **`ProviderTests`.**
  - `Entra_federated_client_credentials_token_writes` requests GitHub's token
    (`ACTIONS_ID_TOKEN_REQUEST_URL`, audience `api://AzureADTokenExchange`)
    and sends it as `client_assertion` to the v2 endpoint.
  - `Google_workload_identity_id_token_writes` requests the token for the
    provider's default audience, then calls STS and `generateIdToken`
    (`includeEmail`).
  - Both assert what they did before.
  - `Configured` skips a leg with none of its identifiers and names every one
    missing. With some present, it fails naming the rest. A configured leg
    without an OIDC token fails too.
  - All three paths were run locally, and their messages are quoted in
    `docs/auth-providers.md` and the PR.
- **`ci.yml`.** `auth (providers)` gains `permissions: id-token: write` (and
  `contents: read`) on the job, and passes the five identifiers. The release
  workflow's call of `ci.yml` already grants `id-token: write`, so the nested
  job's request is allowed there.
- **`auth-providers` declares** `VARVE_ENTRA_TENANT`, `VARVE_ENTRA_CLIENT_ID`,
  `VARVE_ENTRA_AUDIENCE`, `VARVE_GOOGLE_WORKLOAD_IDENTITY_PROVIDER` and
  `VARVE_GOOGLE_SERVICE_ACCOUNT_EMAIL` as environment secrets. repo-standard
  has no environment variables, which is #239.
- **`docs/auth-providers.md`** has the Entra and Google setup, the values,
  and how to see the legs run. `docs/operator/authenticate.md` no longer says
  the job holds secrets. Outside traceability, the two removed names appear
  only in ADR 0223's sentence removing them.
- **Not run: the exchanges against real tenants**, which don't exist yet, and
  **`repo-standard check` against the live repository**, which needs the
  repo-standard App's token from the `gates` environment on `main`. The push
  run of *Repository settings* after each landing is that check. After step
  2 it should report only the five identifiers as "declared and not present".

**Test counts.**

| Suite | Before | After |
|---|---:|---:|
| `RepoStandard.Tests` | 88 passed, 1 skipped | 89 passed, 1 skipped |
| `Varve.Server.Tests` | 183 | 183 (two provider tests renamed, none added) |
| `ProviderTests` here, with no leg configured | 6 skipped | 6 skipped |

**Gates run locally:** `decision-sets` (once the clone was unshallowed),
`required-checks`, `repo-standard-test`, `issue-refs`, and the solution's
Release build. The full `eng/ci.cs` was not run.

**The decision report's baseline** was already 1,271 lines behind `main`
before this change. This change's seven lines were edited in by hand and
checked against a real run, which then differs by 1,264. It was not
regenerated whole, which would have brought unrelated differences in.

## Questions for the maintainer

- **Accepting ADR 0223** and its six rulings. The pull request is red on
  `CS0618` until then, if anything cites them; nothing does.
- **`EntraAndGoogleBySecret`** keeps a name that no longer describes it,
  because a key is never renamed. If you'd rather revoke it and file a new
  key, that is a `revoked-at` with a successor, which the ledger's README
  says is not what `revoked-at` is for.
- **0223's number** is from the gap between readiness A and B. If readiness A
  reserved 0223, say so and it moves.

## Deferred, each as an issue

- [#239](https://github.com/Hafeok/Varve/issues/239): repo-standard declares
  an environment's variables. `auth-providers`' identifiers then move from
  secrets to variables.
- [#241](https://github.com/Hafeok/Varve/issues/241): declare the
  `entra-obo` environment, restricted to `main`, before `entra-obo.yml`'s
  first dispatch creates it unprotected.
- The tenants themselves are the maintainer's setup (`docs/auth-providers.md`),
  not deferred work, so they have no issue.
