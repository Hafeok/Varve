# 0242 — Problems that say what to do

## Status

**Filed unaccepted by the session for #194, 2026-11-10** (ADR 0066).
Acceptance is the maintainer's act, or his delegate's for a decision he has
read (ADR 0239). Records decision set
[`problems-that-say-what-to-do`](../decisions/problems-that-say-what-to-do.md).

Adds members to four types of ADR [0119](0119-headers-and-the-problem-catalogue.md)'s
catalogue, and a section to every page of `docs/problems/`. Changes no
status, no title and no type.

## Context

The heuristic review of the Web UI (findings F02, F03, F04, F17, under #96)
found that problem panels stop at the diagnosis. The console shows the
server's `title` and `detail` as they are and doesn't paraphrase them
(`docs/ui/README.md`, Content). So the next step has to come from the server
and from the catalogue's pages:

- **`forbidden`** said nothing beyond its title. An operator couldn't tell a
  wrong token from a missing grant, or which permission to grant. ADR 0037's
  thin refusal was written for `401`, against a probe that holds no token. A
  `403` goes to a caller whose token already validated.
- **A dataset that failed to open** answered `unavailable` with the store's
  message. A log that doesn't verify read like a transient fault, and the
  console said the server tries again. No retry fixes damage.
- **`writer-superseded`** said that a newer writer holds the dataset, but not
  which one, or whether anything is wrong.
- **`shutting-down`** didn't say that other instances keep serving.

## Decision

1. **A `403 forbidden` names what was needed and who asked.** Members:
   `permission` (the permission checked, without its `varve:` prefix);
   `scope`, `dataset` or `fleet`; `dataset`, naming it, when the scope is
   one; `agent`, the caller's agent IRI as the server resolved it from the
   token, when it resolved one; and `plane`, `data` or `management`. The
   `detail` restates them. It names no grant, no role and no other agent. A
   `401` stays as thin as ADR 0037 made it. The endpoint that refuses writes
   the problem itself, with RFC 6750 §3.1's `WWW-Authenticate`. The host's
   handler still writes the bare problem for a refusal no endpoint decided.

   **The condition, from row-level access (ADRs 0107, 0236, 0237):** a `403`
   names only what the caller sent (the dataset in its path, the permission
   its request needs, and the agent its own token resolves to). It names
   nothing that was looked up, and it never confirms that something hidden
   exists. Where the caller can't see the target, such as a graph outside
   its readable scope or a row-restricted read, the answer stays what it
   was: a hidden graph answers exactly as an absent one, and a dataset the
   caller holds nothing on answers exactly as one that doesn't exist, apart
   from the name the caller sent. `AuthTests` holds this for the Graph
   Store's five methods, for three updates, and for a dataset on each plane.

2. **A dataset that failed to open is classified by whether a retry helps.**
   `failure` is `damaged` (its log doesn't verify:
   `LogVerificationException`), `configuration` (the host's configuration or
   build can't open it: a storage root or object store not configured, or
   `UnsupportedFormatException`) or `transient` (anything else, tried again).
   `DatasetEntry.Failure` carries it. `/manage/datasets`, the admin listing
   and a dataset's `…/state` write it beside `reason`. The `unavailable`
   problem for a failed dataset carries `reason` `failed` and `failure`,
   and its `detail` says what to do: restore from a backup, correct the
   setting or upgrade, or wait.

3. **`writer-superseded` names the writer that holds the epoch.** `writer`
   is the internal address the store's writer record names, and `epoch` its
   epoch. The record is read when the write is refused, through
   `IWriterDirectory.HolderAsync`, whatever the refused instance believes
   its role is. A fleet of one has no record to read and leaves both out.
   The page says which of two cases applies: a takeover or a restart
   elsewhere (send writes there), or two instances that both claim the
   dataset (reconfigure, usually `Varve:Fleet:Writer`).

4. **`shutting-down`'s `detail` says the instance is draining.** In a fleet it
   adds that the other instances keep serving. It names none, because which
   instance will answer is the load balancer's choice.

5. **Every page in `docs/problems/` has a `## What to do` section**: the
   next step for whoever got the problem, with the setting, the `varve`
   command or the guide page that leads there. `ProblemCatalogueTests` fails
   a page without one.

## Alternatives considered

- **Keep `403` thin, as `401` is.** That protects nothing a valid token
  doesn't already have. The caller learns which permission it lacked on a
  dataset it named. Grants, roles and other agents stay hidden. The
  operator, meanwhile, can't act on a bare refusal.
- **Name the missing grant or the roles that would satisfy it.** That
  discloses the grant table to anyone with a token, so it's refused.
- **A `403` naming a hidden graph or row the caller may not write.** That
  would confirm the graph or row exists to a caller who can't read it, so
  it's refused. Such a target keeps its existing answer (`404` for a graph
  the caller can't read, the uniform `row-not-writable`), and that answer
  doesn't depend on whether the target exists.
- **Classify failures from the message text.** Messages are display text
  (`FailureTextIsDisplayText`). The classification reads exception types.
- **New problem types for damage and misconfiguration.** A failed dataset
  is still `unavailable` to a client. The member tells the operator more
  without changing what a client branches on.

## Consequences

- `Varve.Protocol` gains `DatasetFailure`, `DatasetEntry.Failure` and
  `IWriterDirectory.HolderAsync`, with a default implementation. All are in
  `PublicAPI.Unshipped.txt`.
- The console's problem panel can show the new members. That's a #96
  follow-up; until then it shows the `detail`, which states them.
- A refused superseded write reads the store's writer record once. It's an
  error path, bounded by the request.

## Checks

- **Checked against the accepted ADRs.** Touches:
  - **0037:** a `401` unchanged; a `403`'s body states the needed permission
    and the resolved agent;
  - **0119:** members added to four types; titles and statuses unchanged;
  - **0107**, **0236**, **0237:** what a caller can't see stays unconfirmed;
    the condition on ruling 1 holds the `403` to it;
  - **0136**, **0168:** the writer record is read, never written.

  No conflict.
- **Layer ownership.** `Varve.Protocol` (5): the problems and the seam.
  `Varve.Server` (6): the classification, the holder read and the drain
  detail.
- **Analyzer rule.** None. The page section is held by a test.
- **Open questions owned.** The console's rendering of the members (#96).
