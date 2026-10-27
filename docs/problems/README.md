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
| [`entailment-unsupported`](entailment-unsupported.md) | 400 | `value`, `supported` | The server does not answer this request under that entailment regime. |
| [`entailment-refused`](entailment-refused.md) | 400 | `reason` | The entailment regime refuses this query. |
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
| [`conflict`](conflict.md) | 409 | `position`, `expectedPosition`, `headPosition`, `graphs`, `graphsTruncated` | Another commit came first. |
| [`dataset-open`](dataset-open.md) | 409 |  | The dataset is open; close it first. |
| [`event-catalogue-moved`](event-catalogue-moved.md) | 409 | `manage` | The dataset's event declarations are on the management plane. |
| [`precondition-failed`](precondition-failed.md) | 412 | `position`, `expectedPosition`, `headPosition`, `graphs` | The precondition does not name the current version. |
| [`request-too-large`](request-too-large.md) | 413 | `limit` | The request body is over the server's limit. |
| [`unsupported-media-type`](unsupported-media-type.md) | 415 |  | The request body is in a media type or charset this endpoint does not read. |
| [`rejected`](rejected.md) | 422 | `report` | A validator rejected the commit. |
| [`event-declaration-invalid`](event-declaration-invalid.md) | 422 | `declaration`, `reason` | The event declaration is not well formed. |
| [`event-schema-incompatible`](event-schema-incompatible.md) | 422 | `declaration`, `fields` | The declaration change is incompatible with its version. |
| [`event-not-rendered`](event-not-rendered.md) | 422 | `event`, `eventType`, `reason` | An event could not be rendered. |
| [`shapes-violated`](shapes-violated.md) | 422 | `binding`, `shapesGraph`, `findings`, `report`, `shapesChanged` | The commit introduces a violation of a shapes binding. |
| [`binding-not-incremental`](binding-not-incremental.md) | 422 | `binding`, `shapes` | The binding's shapes cannot be validated incrementally. |
| [`shapes-invalid`](shapes-invalid.md) | 422 | `binding`, `problems` | The binding's shapes graph is refused. |
| [`memory-limit-exceeded`](memory-limit-exceeded.md) | 422 | `limit`, `actual` | The request needs more memory than the server allows one request. |
| [`as-of-distance-exceeded`](as-of-distance-exceeded.md) | 422 | `limit`, `actual` | The as-of position is too far from its nearest checkpoint. |
| [`declaration-invalid`](declaration-invalid.md) | 422 | `findings` | The management declarations are not valid. |
| [`too-many-live-tails`](too-many-live-tails.md) | 429 | `limit` | The client holds too many live tails open. |
| [`too-many-requests`](too-many-requests.md) | 429 |  | The client sent more requests than the endpoint allows in its window. |
| [`unavailable`](unavailable.md) | 503 | `reason` | The dataset cannot answer now. |
| [`read-limit-exceeded`](read-limit-exceeded.md) | 503 |  | The read was cut by a server limit. |
| [`shutting-down`](shutting-down.md) | 503 |  | The server is shutting down. |
| [`not-ready`](not-ready.md) | 503 | `management`, `datasets`, `retention` | The server is not ready. |
| [`server-busy`](server-busy.md) | 503 | `limit` | The server has as many reads in flight as it allows. |
| [`writer-superseded`](writer-superseded.md) | 503 |  | A newer writer holds the dataset; this instance serves reads only. |
| [`management-behind`](management-behind.md) | 503 | `position`, `appliedPosition` | The server has not applied the management position asked for. |
| [`dataset-behind`](dataset-behind.md) | 503 | `position`, `appliedPosition` | This instance has not applied the dataset position asked for. |
| [`validation-behind`](validation-behind.md) | 503 | `binding`, `position`, `projectedPosition` | The validation report has not reached the position asked for. |
| [`writer-moved`](writer-moved.md) | 503 | `epoch`, `requestId` | The instance a write was forwarded to no longer writes the dataset. |
| [`reader-forgotten`](reader-forgotten.md) | 503 |  | This instance's reader record was forgotten; it attaches again. |
| [`read-unannounced-extent-gone`](read-unannounced-extent-gone.md) | 503 |  | An unannounced read found an extent gone. |

What each limit's failure looks like to a client is in
[`docs/operator/limits.md`](../operator/limits.md).
