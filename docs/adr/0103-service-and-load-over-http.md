# 0103 — `SERVICE` and `LOAD` over HTTP

## Status

**Accepted — filed unaccepted by milestone 7b of #11, 2026-10-08** (ADR 0066).
Decided by the maintainer on the 7b plan: "the client exposes
`RdfDocumentClient.FetchAsync`, and each host binds `ILoadSource` to it in one
line". Discharges ADR 0055's and ADR 0057's reservations of the HTTP
implementations to milestone 7. Acceptance is the maintainer's act on the
pull request.

## Context

`IServiceHandler` is a contract in `Varve.Sparql.Evaluation` (layer 3, ADR
0055): it is given the `Service` node, the endpoint, the variables and the
incoming solutions, and answers solutions or a failure; the evaluator joins.
`ILoadSource` is a contract in `Varve.Sparql.Store` (layer 5, ADR 0057): it
is given an IRI and answers a document, its syntax and its base. The HTTP
client package is layer 5 (ADR 0102), so it can implement the first and
cannot reference the second: a same-layer reference is a violation.

SPARQL 1.1 Federated Query §2.3 and §3.2 say what `SERVICE` means: the
pattern evaluated at the endpoint as `SELECT * WHERE { P }`, a failure an
error, and with `SILENT` one empty solution. §2.4 is informative: a handler
*may* narrow the request by the incoming solutions.

## Decision

1. **`HttpServiceHandler`** implements `IServiceHandler` in
   `Varve.Protocol.Client`.
   - It serialises `SELECT * WHERE { P }` with `SparqlWriter`, the pattern as
     written, the query's prologue not needed because the algebra carries
     full IRIs.
   - It sends it as `POST` with `application/sparql-query`, asking for
     `application/sparql-results+json` first and `application/sparql-results+xml`
     second, and parses the answer with `SparqlResultsReader`. A `200` with
     another media type, any other status, a body beyond the byte cap or a
     response after the timeout is a failure.
   - The failure is returned, not thrown, with the endpoint and the reason:
     the evaluator then errors without `SILENT` and answers Ω0 with it (ADR
     0055, Federated Query §2.3). The handler itself never consults `SILENT`.
   - The incoming solutions are not pushed to the endpoint. §2.4's `VALUES`
     interplay is recorded as the extension to make when a workload shows it
     pays; correctness does not depend on it, because the evaluator joins.
   - The endpoint is checked against the policy (ADR 0102) before anything is
     sent; a refusal is a failure like any other, naming the reason.
   - The handler is synchronous, as the contract is, over `HttpClient.Send`.
2. **`RdfDocumentClient`** fetches an RDF document by IRI: `GET` with
   `Accept: text/turtle, application/n-triples, application/n-quads,
   application/trig`, the syntax taken from the response's `Content-Type`
   (a `.nt`, `.nq`, `.ttl` or `.trig` path decides when the type is absent
   or generic), the base the request's IRI (RFC 3986 §5.1.3; a `Content-Location`
   does not override it), the bytes capped, the policy consulted first. The
   answer is `RdfDocument`: content, syntax, base, or a failure.
3. **The host binds `ILoadSource` to it in one line**, as it binds
   `ISparqlUpdateExecutor` (ADR 0091): `Varve.Server` and the CLI each hold
   an adapter that calls `FetchAsync` and makes a `LoadedDocument`. This is
   the composition root wiring a concrete choice (ADR 0060), and it is not
   the revisit condition of ADR 0091: no protocol code reaches an
   integration; a host adapts a client to an integration's contract.
4. **Configuration**, in the server (ADR 0101's shape) and as flags in the
   CLI (ADR 0104):

   ```json
   "Federation": { "AllowedEndpoints": [ "https://query.wikidata.org/" ], "AllowPrivateAddresses": false, "Timeout": "00:00:30", "MaxResponseBytes": 104857600 },
   "Load":       { "AllowedSources":   [ "https://example.org/data/" ],   "AllowPrivateAddresses": false, "Timeout": "00:01:00", "MaxResponseBytes": 1073741824 }
   ```

   Each section is one policy and one set of limits. With neither section,
   the server's handler and source are the refusing defaults, and a
   `SERVICE` without `SILENT` or a `LOAD` without `SILENT` fails the request
   saying so.
5. **`LOAD`'s document is parsed by the update executor as today** (`sparql-update-store.md`
   §5.4): the client fetches, the executor parses, and a document that does
   not parse fails the operation.
6. **The suites.** `sparql11/service` (7 cases) runs end to end over HTTP:
   each `qt:serviceData` endpoint of a case is an in-process Kestrel server
   over a memory dataset loaded with that data, the handler under test is
   `HttpServiceHandler` with the policy allowing the manifests' endpoint
   IRIs, and a test-side message handler maps those IRIs' hosts
   (`example1.org`, …) to the loopback servers. The cases keep their
   in-process run through `TestServiceHandler` too (ADR 0055), so the two
   handlers are compared on the same cases. The update suites gain no case:
   `update-silent`'s `LOAD` cases need a source that refuses, which the
   policy's empty default is, and the harness's file source stays for the
   suites' published IRIs.

## Alternatives considered

- **Move `ILoadSource` down to layer 2 or 3** so the client can implement it.
  It is on ADR 0057's accepted surface (`UpdateSingleEntryPoint`), and a
  contract whose only implementers are the suite's file source and one HTTP
  client has no second home that would not be a `Common`.
- **`Varve.Protocol.Client` at layer 6.** Nothing could reference it.
- **Push incoming bindings as `VALUES`** (Federated Query §2.4). Fewer rows
  back for a selective outer pattern, more requests and a request size that
  grows with the incoming set. Recorded, not built.
- **Throw on failure** from the handler. ADR 0055 made the handler's failure
  a returned value so the evaluator decides on `SILENT`; a thrown exception
  is treated the same way, and a returned one carries a cleaner message.

## Consequences

- A federated query reaches only the endpoints an operator listed, and only
  over `http` and `https`, with the private ranges closed by default.
- `LOAD` works in the server and the CLI for listed sources; the suites'
  `LOAD` cases are unchanged.
- The service-description's `sd:feature sd:BasicFederatedQuery` is advertised
  only when the server's federation section lists an endpoint (ADR 0106's
  description change).

## Checks

- **Checked against the accepted ADRs** (0001–0101) and specification 1.6.
  Touches:
  - **0055**: the handler's contract, unchanged; its HTTP implementer arrives;
  - **0057**: `ILoadSource` unchanged; its HTTP source arrives by adapter;
  - **0060**: the host binds the choice;
  - **0091**: not its revisit condition, as point 3 argues;
  - **0102**: the policy.

  No conflict.
- **Layer ownership.** `Varve.Protocol.Client` (5); the adapters are the
  hosts' (6).
- **Analyzer rule.** None.
- **Open questions owned.** None.
