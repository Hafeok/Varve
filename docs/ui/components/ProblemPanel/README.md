# ProblemPanel

An RFC 9457 problem from the server, rendered from its type: the status, the server's `title` verbatim, the `detail` if any, the type's extension members, the request id, and a link to the type's page under `docs/problems/`.

- **Consumer provides:** the problem body as received. Known members render by kind: positions as `PositionStamp`, each naming its log, with an expected position and the head as one pair ("expected management position 412 / now 415"); the graphs a conflict names (`graphs`) as IRIs under "Changed since you read it", each once. Unknown members render as key-value pairs in `code`, so a problem type added to the server later still shows everything.
- **On a `403`,** it says who the request was made as, under the title: "Signed in as `<agent IRI>`", the agent the plane's token names, so the operator can tell a wrong token from a missing grant (review F02).
- Replaces the view's body (a `403` on a dataset), or sits inside the dialog whose write failed (a `412` on a state change).
- The request id (`instance`, also `Varve-Request-Id`) is always shown with a Copy button: it is what an operator searches the server's logs and traces for.
