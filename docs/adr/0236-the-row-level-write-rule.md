# 0236 — The row-level write rule

## Status

**Filed unaccepted by milestone 9, part 1 (#202, under #14), 2026-10-30**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on the plan in #14 (2026-10-30), decision 2, the strict
rule. W4 and W5 were added the same day, with ADR 0230's object half.
**Amends ADR [0119](0119-headers-and-the-problem-catalogue.md)**'s catalogue:
`row-not-writable`.

Records decision set
[`the-row-level-write-rule`](../decisions/the-row-level-write-rule.md).

## Context

A caller must not write a quad about a subject it cannot read. More than
that, **a write's outcome must never depend on a row the writer cannot
read**. Otherwise a writer probes for a subject by writing about it and
watching the answer, which is ADR 0190 point 7's lesson. PostgreSQL's row
security documents the residue of a weaker rule: a unique key's violation
reveals that a hidden row exists. The maintainer chose the strict rule.

## Decision

**When the writable subject set of the binding B is `all`, only ADR 0107's
graph check applies.** Otherwise, for a row-restricted writer W, with R its
readable subject set and Ws its writable one, every quad it asks to add or
retract in a graph of Γ_B meets every rule below. One that does not fails
the whole request with **`403 row-not-writable`** and nothing is committed.
The problem body is the same for every rule and every reason, and names no
node, no subject and no binding.

- **W1, the row before.** For the quad's subject s at the pinned position p:
  subjects_p(s) is non-empty and ⊆ Ws, or s has no row in Γ_B.
- **W2, the row after.** At the staged position p′: subjects_p′(s) is
  non-empty and ⊆ Ws, or s has no row left in Γ_B.
- **W3, creation.** A subject with no row in Γ_B before may be written only
  when it is either:
  - a fresh blank node of the request (ADR 0098: a request's labels are
    always fresh), or
  - an IRI under one of W's `createUnder` prefixes.

  So **a named IRI outside them is refused whether or not anything is
  there**, and absent and hidden cannot be told apart.
- **W4, the object.** For an added quad whose predicate is not a public link
  and whose object o is an IRI or a blank node not fresh in the request:
  - o has a row in Γ_B at p′ that W reads, meaning subjects_p′(o) ⊆ R,
    including an unclassified row;
  - and about_p′(q) ⊆ Ws.

  An object with no row in Γ_B is refused as a hidden one is. A triple-term
  object meets W4 for each component, recursively.
- **W5, retractions read the visible view.** A requested retraction of a
  quad W does not see is a no-op, the same as a retraction of an absent
  quad. Every pattern, as in ADR 0107 point 6, evaluates over W's
  `RowScopedQuadSource`. A Graph Store `PUT` or `DELETE`, and a `CLEAR`,
  retract only the quads W sees.
- **Requested, not only effective.** W1 to W4 are checked on the quads the
  request asks for at the pinned position, before the effective delta drops
  an addition already present. Otherwise an addition of a hidden quad that
  exists would be a silent no-op, where an absent one is refused. They are
  checked again on the effective delta under the sequencer, by a validator
  of the request (ADR 0058), against the true head.

**Why the outcome depends only on what W reads.**
- If W1 and W4 hold, every row that feeds subjects(s) and subjects(o) is
  visible to W, by ADR 0230 point 5 and ADR 0234's P1.
- If they do not, the node is hidden, mixed, unclassified or absent, and all
  of these are refused with one body.
- W3 removes the absent-versus-hidden case for creation, and W5 for
  retraction.
- **What remains**: a collision, under W's own `createUnder` namespace, with
  a row only a writer whose subject set is `all` could have put there. It is
  stated, not finessed.

**Effects on other rows are not checked.** Re-linking a row W owns changes
the subjects of rows that link to it. Those changes stay within what W owns
and its outcome does not depend on them. That is what a link means.

**Unclassified rows are read-only to a row-restricted writer**, by W1. Writing
one would make "absent versus hidden" observable.

## The property

For generated pairs of datasets that differ only in quads the writer does not
see, the same request has the same outcome over both:
- accepted or refused;
- the problem body, byte for byte;
- the commit's delta, restricted to what the writer sees.

The requests are generated over SPARQL Update and the Graph Store, and the
pairs over generated classifier bindings and grants. They include:
- absent, hidden, mixed and unclassified nodes, as subjects and as objects;
- IRIs under and outside `createUnder`;
- fresh blank nodes;
- retractions of hidden quads;
- escalation attempts, such as adding `alice ex:caseworker <me>` to a row W
  cannot read.

## Alternatives considered

- **The relaxed rule**, which accepts creation at any absent IRI and states
  the disclosure. The maintainer chose the strict rule.
- **Accept a write about a hidden subject and hide the result.** The writer
  then probes by reading its own write back.
- **Refuse with a body that says why.** Each reason would be a probe.

## Consequences

- **A row-restricted writer creates rows** only at fresh blank nodes or under
  `createUnder`, and **links outside Γ_B** only under public links. The
  operator's guide says how to declare both.
- **`docs/problems/row-not-writable.md`** is added, and `ProblemCatalogue`
  gains the type.

## Checks

- **Checked against the accepted ADRs** (0001–0190). Touches:
  - **0057**: requests and their composed delta;
  - **0058**: the validator of a request;
  - **0098**: fresh labels;
  - **0107**: points 6 and its patterns;
  - **0119**: amended.

  No conflict.
- **Layer ownership.** The rule is `Varve.Rdf`'s (`RowWriteRule`, over ADR
  0231's types). It is applied by `Varve.Sparql.Store` (5) and
  `Varve.Protocol` (5), and composed by the server (6).
- **Analyzer rule.** None.
- **Open questions owned.** None.
