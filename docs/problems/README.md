# Problem types

Every error the server answers is an RFC 9457 problem whose `type` is an IRI
under `https://w3id.org/varve/problems/` (ADR 0092), with its `title`, its
`status` and its extension members fixed per type by `ProblemCatalogue` in
`Varve.Protocol` (ADR 0119). Each type has a page here, which its IRI
resolves to; a test enumerates the catalogue against these pages. `instance`
is the request id, also carried as `Varve-Request-Id`.

| Type | Status | Members | Title |
|---|---:|---|---|
| [`sparql-syntax`](sparql-syntax.md) | 400 | `line`, `column`, `offset` | The request does not parse. |
| [`rdf-syntax`](rdf-syntax.md) | 400 |  | The RDF body does not parse. |
| [`bad-request`](bad-request.md) | 400 |  | The request is not one the protocol accepts. |
| [`operation-failed`](operation-failed.md) | 400 |  | An operation failed; nothing was committed. |
| [`unauthorized`](unauthorized.md) | 401 |  | The request carries no valid bearer token. |
| [`forbidden`](forbidden.md) | 403 |  | The caller does not hold the permission the request needs. |
| [`agent-required`](agent-required.md) | 403 |  | The commit records its agent, and the caller has none. |
| [`graph-not-writable`](graph-not-writable.md) | 403 | `graph` | The request changes a graph outside the caller's writable scope. |
| [`dataset-not-found`](dataset-not-found.md) | 404 |  | No dataset by that name. |
| [`not-found`](not-found.md) | 404 |  | Nothing is served at that address. |
| [`graph-not-found`](graph-not-found.md) | 404 |  | No graph by that name holds a quad. |
| [`position-not-reached`](position-not-reached.md) | 404 | `headPosition` | The position is after the head. |
| [`before-first-commit`](before-first-commit.md) | 404 |  | The time is before the first commit. |
| [`below-archive-horizon`](below-archive-horizon.md) | 404 | `horizon` | The position is below the archive horizon. |
| [`method-not-allowed`](method-not-allowed.md) | 405 |  | The endpoint does not serve this method. |
| [`not-acceptable`](not-acceptable.md) | 406 |  | Nothing in Accept is a format this endpoint writes. |
| [`conflict`](conflict.md) | 409 | `position`, `expectedPosition`, `headPosition` | Another commit came first. |
| [`dataset-exists`](dataset-exists.md) | 409 |  | A dataset by that name exists. |
| [`dataset-open`](dataset-open.md) | 409 |  | The dataset is open; close it first. |
| [`precondition-failed`](precondition-failed.md) | 412 | `position`, `expectedPosition`, `headPosition` | If-Match does not name the head. |
| [`request-too-large`](request-too-large.md) | 413 | `limit` | The request body is over the server's limit. |
| [`unsupported-media-type`](unsupported-media-type.md) | 415 |  | The request body is in a media type or charset this endpoint does not read. |
| [`rejected`](rejected.md) | 422 | `report` | A validator rejected the commit. |
| [`memory-limit-exceeded`](memory-limit-exceeded.md) | 422 | `limit`, `actual` | The request needs more memory than the server allows one request. |
| [`as-of-distance-exceeded`](as-of-distance-exceeded.md) | 422 | `limit`, `actual` | The as-of position is too far from its nearest checkpoint. |
| [`too-many-live-tails`](too-many-live-tails.md) | 429 | `limit` | The client holds too many live tails open. |
| [`unavailable`](unavailable.md) | 503 |  | The dataset cannot answer now. |
| [`read-limit-exceeded`](read-limit-exceeded.md) | 503 |  | The read was cut by a server limit. |
| [`shutting-down`](shutting-down.md) | 503 |  | The server is shutting down. |
| [`not-ready`](not-ready.md) | 503 | `datasets` | The server is not ready. |
| [`server-busy`](server-busy.md) | 503 | `limit` | The server has as many reads in flight as it allows. |

What each limit's failure looks like to a client is in
[`docs/operator/limits.md`](../operator/limits.md).
