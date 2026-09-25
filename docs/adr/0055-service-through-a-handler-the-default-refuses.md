# 0055 — `SERVICE` through a handler, and the default refuses

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the milestone 5b plan.
Settles item 5 of what milestone 5a left to 5b: the `Service` node exists in
the algebra (`sparql-algebra.md` §2.4), and the shape of its evaluation, and
of its refusal, is decided here.

## Context

SPARQL 1.1 Federated Query §3.2 defines `eval(D(G), Service(IRI, P,
SilentOp))` as the solutions of executing `SELECT * WHERE Q`, `Q` the
serialisation of `P`, at the endpoint named by the IRI, "in case of a
successful service invocation according to the SPARQL protocol, and otherwise
Ω0 in case SilentOp is true, and otherwise error". §2.3 says the same in
prose.

Invoking an endpoint is HTTP, and HTTP is a host's: the evaluator at layer 3
runs over `IQuadSource` and knows no transport (ADR 0005 draws that line for
the store, and the same argument holds for the network). The server at
milestone 7 is where the protocol client belongs, at layer 5. Until then —
and in the browser, and in any embedded host that forbids outbound calls —
the evaluator must still do something defined with a `SERVICE` pattern.

## Decision

**`IServiceHandler` is a contract in `Varve.Sparql.Evaluation`**, over
`Varve.Rdf` types and the algebra:

```csharp
public interface IServiceHandler
{
    ServiceResult Execute(ServiceRequest request, CancellationToken cancellationToken);
}
```

- **`ServiceRequest`** carries the `Service` node as written (its name, its
  pattern, its `SILENT` flag), the endpoint as an IRI term, and the incoming
  solutions — the ones the evaluator will join the result with — as
  `RdfTerm`s over the variables they bind. Materialising at this boundary is
  deliberate: a handler talks to something that does not share the source's
  handles, and the terms are what a protocol client would serialise.
- **`ServiceResult`** is either solutions (variables and rows of `RdfTerm?`)
  or a failure with a message. The evaluator **joins** the solutions with the
  incoming ones, so a handler may use the incoming solutions to narrow what
  it asks for — Federated Query §2.4's `VALUES` interplay — or ignore them;
  either is correct. A handler that throws has failed, in the same sense.
- **A `SERVICE ?v` pattern** — Federated Query §4, informative — is invoked
  once per distinct IRI that `?v` has in the incoming solutions, each result
  joined with the solutions that bound that IRI. An incoming solution that
  leaves `?v` unbound or binds it to something other than an IRI is a
  failure of that invocation.
- **The default handler refuses.** `EvaluationOptions.ServiceHandler` is a
  handler whose every result is a failure naming the endpoint. Nothing in the
  default configuration opens a connection.
- **On failure without `SILENT`, the query fails** with a
  `QueryEvaluationException` whose message names the endpoint IRI and carries
  the handler's message. This is the "otherwise error" of Federated Query
  §3.2 and the "the query will stop and return the error" of §2.3.
- **On failure with `SILENT`, the `Service` evaluates to Ω0**, the multiset
  holding one empty solution mapping, per Federated Query §2.3: "The SILENT
  keyword indicates that errors encountered while accessing a remote SPARQL
  endpoint should be ignored while processing the query. The failed SERVICE
  clause is treated as if it had a result of a single solution with no
  bindings." Joined with the incoming solutions, Ω0 leaves each of them as it
  was.
- **The HTTP implementation is the server's**, at milestone 7, at layer 5. It
  implements this interface; nothing here changes when it arrives.

## Alternatives considered

- **Refuse `SERVICE` at compile time**, as 5a expected. Simple. Rejected: it
  makes `SERVICE SILENT` fail although §2.3 defines its failure as success
  with Ω0, and it gives an embedded host no way to federate to a local
  source without the server.
- **A handler that receives the serialised query text**, as the protocol
  would. Closer to the wire. Rejected: every handler would re-parse what the
  evaluator already holds as algebra; a protocol client serialises with
  `SparqlWriter` when it needs text, and a local handler — a test, or a
  federation to another in-process dataset — never does.
- **Handles instead of terms at the boundary.** No materialisation. Rejected:
  the handles are the source's, and a remote endpoint's terms are not in the
  source; the evaluator would have to internalise them anyway, which it does
  on the way back.
- **Streaming results** through the contract. Rejected for now: the evaluator
  joins the result with the incoming solutions, which needs it held, and a
  streaming variant is an additive member when a handler needs one.

## Consequences

- **`SERVICE` works in tests without a network**: the conformance harness
  wires `sparql11/service` through a handler that evaluates the pattern over
  the manifest's `qt:serviceData` with this same evaluator.
- **A `SERVICE` without `SILENT` fails every query in the default
  configuration**, and says which endpoint. That is the honest report of a
  configuration with no federation.
- **The evaluator's public surface gains three types** — the interface, the
  request and the result — plus the refusing default.

## Checks

- **Checked against the accepted ADRs** (0001–0054) and the specification.
  Touches **0005** (no transport below layer 5), **0048** (the handler sees
  the algebra node; VARVE0007's reserved wording is amended by dated note in
  ADR 0004 to admit contracts over algebra types), **0052** (the handler gets
  the same cancellation token), and **0003** (a handler is an option, not a
  registry). No conflict with any.
- **Layer ownership.** The contract and the refusing default are
  `Varve.Sparql.Evaluation`, **layer 3**. The HTTP handler is **layer 5**, at
  milestone 7.
- **Analyzer rule.** None.
- **Open questions owned.** None.
