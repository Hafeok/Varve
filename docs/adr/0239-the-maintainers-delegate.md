# 0239 — The maintainer's delegate

## Status

**Accepted — filed unaccepted by the landing session for #244, 2026-11-06,** at the
maintainer's direction of that day (ADR 0066). By the same direction it is
applied from 2026-11-06, ahead of its acceptance. The maintainer read it on
its pull request, #245, as it stood at 8d2afa78, and its rulings were accepted
there on 2026-11-06, by his delegate at his word.

**Amends ADR [0066](0066-expected-red-pull-requests.md)** (a session never
accepts) and **ADR [0087](0087-the-identity-map.md)** (who approves and
accepts for an agent), each by a dated amendment in its own file. Records
decision set [`the-maintainers-delegate`](../decisions/the-maintainers-delegate.md).

**Planned end:** with ADR 0087's. When an approval is a signed ledger `Review`
by an identity holding `Approve`, a delegate needs an identity and a grant of
its own, and this ADR is superseded.

## Context

Two acts are the maintainer's alone. A session's pull request lands on an
approval of its head by its responsible human (ADR 0087 rule 3), and a
decision a session files is accepted by that human, never by a session
(ADR 0066).

The landing run of 2026-11-02 to 2026-11-06 brought some twenty pull requests
to `main` in a fixed order. Every merge of `main` into a branch made a new
head, which needed a new approval. Every key a session restated needed a new
acceptance. The maintainer gave those by telling one session to, in
conversation, on his own machine, with his own key. So an approval comment or
an `accepted-by` line from that run reads as his own hand when the session
wrote it at his word.

That delegation was real and nothing recorded it. This ADR records who may
act for the maintainer, what it may do, and how its acts are told from his.

## Decision

1. **There is one delegate, defined by where it runs.** It is a Claude Code
   session on the maintainer's own machine, signing with his YubiKey. A cloud
   session is not one, and neither is a session on another machine or with
   another key. The maintainer starts it and directs it, and it is his
   delegate while he says so in that session.
2. **It may do three things for him:**
   - approve a pull request's head;
   - land it, by `eng/land.cs`, to `land/<name>` and then to `main`;
   - write `accepted-by`, naming him, **for a decision he has read**.

   It decides nothing for him. A ruling is still his, and a decision he has
   not read waits for him. It tags nothing, and it does not change the
   identity map.
3. **Its approvals read `approve <sha> (delegate)`.** The gates read the
   comment as the maintainer's, because it is posted under his login and
   `eng/agent-review.cs` reads `approve <sha>`. The suffix is for a reader.
4. **A commit it makes for him carries
   `Delegated-by: Emil Okkels Klein <emil@okkels-klein.dk>`.** That covers:
   - a commit that accepts a decision;
   - a commit made to land a pull request: a merge of `main` into its branch,
     and what that merge needs to pass, such as a renumbered descriptor or
     regenerated lockfiles and digests.

   Such a commit is authored in his name, signed with his key and signed off
   as his, as it was before. The trailer says the session wrote it.
5. **Its own implementation work is the agent's.** A commit that contains
   work the session wrote is authored `Claude (Emil)
   <claude+emil@okkels-klein.dk>`, the agent of the identity map, and signed
   off by the maintainer, as a cloud session's is. `agent review` then judges
   it as it judges any agent's commit.
6. **From 2026-11-06.** History before that date is left as it is. Approvals
   and acceptances the session wrote earlier, at the maintainer's word,
   carry no marker.

## Alternatives considered

- **Name the session in `eng/identities.json` as a delegate.** Rejected. The
  map's delegates are humans with a login and a key. The session has neither:
  it acts through the maintainer's. An entry would claim a separation that
  does not exist.
- **A cloud session with standing permission to land.** Not pursued. The
  maintainer's credentials stay on his machine.
- **Leave it unrecorded.** Rejected. The acts happen either way, and then
  nothing says which of them were the session's.

## Consequences

- **No gate can tell the delegate's act from the maintainer's.** Both come
  from his login and his key. The suffix, the trailer and the agent identity
  are the record, and the session writes them. Checking that record is the
  maintainer's.
- **The delegate can approve a head that holds its own work.** Point 5 makes
  such work visible as the agent's, and point 3 makes the approval visible as
  the delegate's. Nothing prevents it.
- **The delegation does not outlive the session.** A later session is the
  delegate only when the maintainer says so in it.
- **`AGENTS.md` changes with this ADR:** `accepted-by` is written by the
  maintainer, or by his delegate for a decision he has read, and by no other
  session.

## Checks

- **Checked against the accepted ADRs.** Touches:
  - **0033:** every commit still carries its issue reference, and the session
    still files its traceability record.
  - **0034:** the delegate's commits are signed with the maintainer's key.
    The sandbox exception for cloud sessions is unchanged.
  - **0066:** amended. A session other than the delegate still never accepts.
  - **0087:** amended. The map, its three rules and its gates are unchanged.
  - **0088:** `main` still takes only checked commits, through `land/`.
- **Layer ownership.** None.
- **Analyzer rule.** None. The gates of ADR 0087 read the delegate's acts as
  the maintainer's.
- **Open questions owned.** None.
