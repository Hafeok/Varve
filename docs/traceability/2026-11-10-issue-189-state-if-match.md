# If-Match on the state route

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#189](https://github.com/Hafeok/Varve/issues/189) |
| **Date** | 2026-11-10 |
| **Tool** | Claude Code 2.1.322, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYNvBGThZuSuStrnN2Z` |
| **Branch** | `claude/happy-heisenberg-v8vg11-189`, from `main` at 2f7573d8 |
| **ADRs** | none filed or amended; [0163](../adr/0163-the-management-plane.md) point 6 already requires it |

## The prompts

> Thanks. Take the three server follow-ups, one pull request each, starting from a fresh main: #189 first (If-Match on the state route, with the same ETag the declaration route uses), then #193, then #194. Post your plan on each issue before building. You are not the delegate: file any decision unaccepted, post your heads and ask. Part 2 (#195–#199) waits for its own prompt.

> Take #262 first: the intermittent 400 rdf-syntax on Graph Store POST. Start with the evidence it asks for, so we know whether the runner sent a short body or the server parsed a short read. Then #189, #193 and #194 as before.

## The report

The plan was posted on the issue before building.

**What was changed.**
- `PUT /manage/datasets/{name}/state` takes `If-Match` with the declaration
  graph's version, as `PUT /manage/datasets/{name}` does. It uses the same
  `TryExpected` and `CommitAsync`. A stale tag is `412 precondition-failed`
  naming the graph, a malformed or multiple tag is `400`, and `*` or no
  header applies. A takeover body is unchanged: it belongs to the store
  (ADR 0179), not to the declaration.
- `GET …/state` sends the same `ETag` as `GET /manage/datasets/{name}`, so a
  client that read the state can write it back conditionally. This is the
  one addition to what the issue asked, and the plan flagged it.
- `docs/operator/datasets.md` (the `…/state` row) and
  `docs/problems/precondition-failed.md` say so.

**Tests.** `ManagementTests.The_state_route_sends_the_declarations_version_and_a_put_honours_if_match`,
and `FleetTests.A_state_write_through_the_follower_is_held_to_its_if_match_by_the_writer`
for a write forwarded by a follower. The fleet test ran against Azurite
3.37.0 (the upstream image; the CI mirror needs registry credentials this
session doesn't have). Its first version failed: it used `"1"` as the stale
tag, and in a fresh fleet `1` is the current version. It now takes one below
the current version, as the management test does. Fleet, management and
slow-sender: 28 passed. The rustfs leg of the fleet test wasn't run locally.

**No new decision.** ADR 0163 point 6 already says management `PUT`s take
`If-Match` with the resource graph's version. The plan offered a dated
amendment naming the state route, if the maintainer wants one.
