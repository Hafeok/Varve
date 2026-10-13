# `conflict`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/conflict` |
| **Title** | Another commit came first. |
| **Status** | `409` |
| **Members** | `position`, `expectedPosition`, `headPosition`, `graphs`, `graphsTruncated` |

The sequencer's `Conflict`: another commit came first at the position the write expected. `headPosition` and `position` are the head; `expectedPosition` is what `If-Match` named, when it did (ADRs 0011, 0094).

Since ADRs 0125 and 0126 a write conflicts only with commits that touched its scope: the graph of a Graph Store write, the graphs `Varve-If-Graphs-Match` named, or what an update read and wrote. `graphs` lists those commits, newest first, as `{"position": n, "graphs": ["<iri>" | "default", …]}`, each with the graphs of the scope it touched — or, for a write with no scope, the graphs it touched that the caller may read, so the list never names a graph the caller cannot see (ADR 0107). At most 100 commits are listed, read at most `Varve:Limits:MaxAsOfDistance` commits back from the head; `graphsTruncated` says the list stopped short.

```json
{
  "type": "https://w3id.org/varve/problems/conflict",
  "title": "Another commit came first.",
  "status": 409,
  "position": 42,
  "headPosition": 42,
  "expectedPosition": 40,
  "graphs": [{ "position": 41, "graphs": ["https://example.org/orders/7"] }],
  "graphsTruncated": false
}
```

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
