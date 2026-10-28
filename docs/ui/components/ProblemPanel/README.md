# ProblemPanel

An RFC 9457 problem from the server, rendered from its type: the status, the server's `title` verbatim, the `detail` if any, the type's extension members, the request id, and a link to the type's page under `docs/problems/`.

- **Consumer provides:** the problem body as received. Known members render by kind (positions as `PositionStamp`, `graph` and `graphs` as IRIs); unknown members render as key-value pairs in `code`, so a problem type added to the server later still shows everything.
- Replaces the view's body (a `403` on a dataset), or sits inside the dialog whose write failed (a `412` on a state change).
- The request id (`instance`, also `Varve-Request-Id`) is always shown with a Copy button: it is what an operator searches the server's logs and traces for.
