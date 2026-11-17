# /manage/system names the instance that answered

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#193](https://github.com/Hafeok/Varve/issues/193) |
| **Date** | 2026-11-10 |
| **Tool** | Claude Code 2.1.322, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYNvBGThZuSuStrnN2Z` |
| **Branch** | `claude/happy-heisenberg-v8vg11-193`, from `main` at 2f7573d8 |
| **ADRs** | none filed or amended; a member added to the resource of ADR 0163 point 13, read as ADR 0203 types reads |

## The prompts

> Thanks. Take the three server follow-ups, one pull request each, starting from a fresh main: #189 first (If-Match on the state route, with the same ETag the declaration route uses), then #193, then #194. Post your plan on each issue before building. You are not the delegate: file any decision unaccepted, post your heads and ask. Part 2 (#195–#199) waits for its own prompt.

> Take #262 first: the intermittent 400 rdf-syntax on Graph Store POST. Start with the evidence it asks for, so we know whether the runner sent a short body or the server parsed a short read. Then #189, #193 and #194 as before.

## The report

The plan was posted on the issue before building.

**What was changed.**
- `ManagementStatus.Instance` is an init property, in `PublicAPI.Unshipped.txt`.
  The fleet sets it from the instance name it already uses for placement,
  hop headers and request ids (`Varve:ObjectStorage:Instance`, else the
  machine name).
- `GET /manage/system` writes `instance` first. The route is never
  forwarded, so a follower names itself.
- `SystemPlane.Instance` is an `InstanceName?` in `Varve.Protocol.Client`,
  and `instance` no longer lands in `Members`.
- The console's shell reads the typed property: "varve-1 · system writer".
  The role alone is kept only for a server that predates the member.
- `docs/operator/datasets.md` lists the member, and the Shell component's
  README no longer says "until #193".

**Tests.** `ManagementTests` checks that the instance named matches the
prefix of the response's `Varve-Request-Id`. A new fleet test,
`Each_instance_names_itself_in_the_management_system_read_the_follower_included`,
has instances `a` and `b` each answer their own name and role (run against
Azurite 3.37.0). `ShellFactsTests` gained a test that the client reads the
member as typed, and that a reply without it reads as null. Results:
Fleet, management, health and auth, 69 passed; shell facts, 6 passed. The
solution builds in Release with 0 warnings.
