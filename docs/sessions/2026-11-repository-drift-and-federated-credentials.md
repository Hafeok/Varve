# Session, 2026-11-06: repository drift and federated provider credentials

Developed with AI assistance under human review. The full record, with the
prompt, is
[`docs/traceability/2026-11-06-issue-101-repository-drift-and-federated-provider-credentials.md`](../traceability/2026-11-06-issue-101-repository-drift-and-federated-provider-credentials.md).

## Pull requests

| PR | Step | Closes |
|---|---|---|
| [#238](https://github.com/Hafeok/Varve/pull/238) | 1: the declaration catches up: labels `adr`, `soak`, `spike`; environment `azure-objects` restricted to `main`; repo-standard compares a ruleset rule at its default parameters as the rule without them | — (`Refs #101`) |
| [#242](https://github.com/Hafeok/Varve/pull/242), stacked on #238 | 2: ADR 0223, the provider legs by federated credentials, `auth-providers` declaring identifiers only, `docs/auth-providers.md` | #101 |

## ADR

**0223**, *The provider legs by federated credentials*, filed unaccepted. It
supersedes ADR 0100's point 4 in part. 0100's body is not edited, and its
Status line names the supersession.

## Issues

- **Closed:** [#101](https://github.com/Hafeok/Varve/issues/101), by step 2's
  declaration commit, once the declaration converges.
- **Opened:**
  - [#239](https://github.com/Hafeok/Varve/issues/239), environment variables
    in repo-standard;
  - [#241](https://github.com/Hafeok/Varve/issues/241), declare `entra-obo`
    before its first dispatch.

## Test counts

| Suite | Before | After |
|---|---:|---:|
| `RepoStandard.Tests` | 88 passed, 1 skipped | 89 passed, 1 skipped |
| `Varve.Server.Tests` | 183 | 183 |

## The `land` finding

It was a normalisation difference in repo-standard, not drift. GitHub's REST
`GET` leaves `update_allows_fetch_and_merge` out at its default, `false`,
which is what the `land/` flow needs. The comparison now treats the two
spellings as one rule.

## Questions

- Accept ADR 0223 and its rulings.
- `EntraAndGoogleBySecret` keeps its name, because a key is never renamed.
- 0223 was taken from the gap between readiness A (0213–0222) and B (0224 on).

## Deferred

- [#239](https://github.com/Hafeok/Varve/issues/239): the identifiers move to
  variables once repo-standard can declare them.
- [#241](https://github.com/Hafeok/Varve/issues/241): `entra-obo` declared
  before it exists.

## Expected state after landing

`repo-standard check` reports the five `auth-providers` identifiers as
"declared and not present" until the maintainer sets up the tenants by
`docs/auth-providers.md`. That is setup, not drift. Both provider legs skip
until then.
