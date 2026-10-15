# `event-declaration-invalid`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/event-declaration-invalid` |
| **Title** | The event declaration is not well formed. |
| **Status** | `422` |
| **Members** | `declaration`, `reason` |

A commit changed an event catalogue graph, and the result is not a well-formed
declaration (ADR 0145, `docs/spec/events.md` §7.1). Nothing was committed.

- `declaration` is the type node being written, or the graph when no node is
  in question.
- `reason` is one of:
  - `graph`: a graph under the catalogue namespace that is not a source graph's catalogue;
  - `blank-node`: a blank node in a catalogue graph;
  - `term`: a term missing, given twice or of the wrong kind;
  - `select`: a query that does not parse, or is not of the rendering's form;
  - `field`: a field given twice, or of two types;
  - `class-taken`: a class declared twice in one catalogue graph;
  - `name-taken`;
  - `removed`: a declaration retracted, which is never done; retire it with `varve:eventRetired true`;
  - `version`: a version lower than before.

**`name-taken` says nothing more.** It means the short name is another
declaration's, in this dataset. It never names that declaration, its graph,
its class or its version, so a caller learns nothing of a graph it cannot read.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
