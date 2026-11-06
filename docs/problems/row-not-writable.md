# `row-not-writable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/row-not-writable` |
| **Title** | The request writes a row outside the writable subject set. |
| **Status** | `403` |
| **Members** | none |

A row-restricted writer asked to add or retract a quad that the row-level
write rule refuses (ADR 0236), and nothing was committed. The rule is:
- the quad's subject has a row of the writer's own subjects;
- a subject with no row is created only as a fresh blank node or under the
  writer's `varve:createUnder` prefixes;
- an object under a predicate that is not a public link has a row the
  writer reads.

The body is the same for every reason, and names no node, subject or
binding, so that a refusal never says more than what the writer reads. A
graph the writer may not write at all is `graph-not-writable` instead. See
`docs/operator/row-level-access.md` for declaring public links and
`createUnder`.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
