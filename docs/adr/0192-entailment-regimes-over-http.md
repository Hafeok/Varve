# 0192 — Entailment regimes over HTTP: the `Varve-Entailment` header and the service description

## Status

**Accepted — filed unaccepted by Inference, part 1 (#170, under #95),
2026-10-27** (ADR 0066). Decided by the maintainer on the plan in #95
(2026-10-27), question 3b. Acceptance is the maintainer's act on the pull
request. Amends, each by a dated amendment:
- **ADR [0119](0119-headers-and-the-problem-catalogue.md)**: the
  `entailment-unsupported` and `entailment-refused` problems, and the
  header in the `Vary` list;
- **ADR [0132](0132-cors.md)**: the header joins both CORS lists.

## Context

- SPARQL 1.1 Protocol defines no parameter that selects an entailment regime,
  and SPARQL 1.1 Entailment Regimes is silent on how a client selects one
  (§1.4).
- The only standard surface is the service description's advertisement
  (Service Description §3.2.3–3.2.6). Its terms attach to `sd:Service` and
  `sd:NamedGraph` only:
  - `sd:defaultEntailmentRegime` and `sd:defaultSupportedEntailmentProfile`
    on the service;
  - `sd:entailmentRegime` and `sd:supportedEntailmentProfile` on a named
    graph.
- Varve already selects the read position by a request header,
  `Varve-As-Of` (ADR 0096). Its service description advertises
  `sd:defaultEntailmentRegime ent:Simple`.

## Decision

1. **`Varve-Entailment`, a request header**, read by the query endpoint.
   - **Its value** is one token, or one regime IRI in angle brackets:

     | Token | Regime | Answered by | Available from |
     |---|---|---|---|
     | `simple` | `http://www.w3.org/ns/entailment/Simple` | evaluation as today | #170 |
     | `rdf` | `http://www.w3.org/ns/entailment/RDF` | rewriting (ADR 0193) | #170 |
     | `rdfs` | `http://www.w3.org/ns/entailment/RDFS` | rewriting (ADR 0193) | #170 |
     | `d` | `http://www.w3.org/ns/entailment/D` | rewriting, Varve.Xsd's datatype map (ADR 0193) | #170 |
     | `owl2-rl` | `http://www.w3.org/ns/entailment/OWL-RDF-Based`, profile `http://www.w3.org/ns/owl-profile/RL` | the closure | #171 |
     | `owl2-ql` | `http://www.w3.org/ns/entailment/OWL-Direct`, profile `http://www.w3.org/ns/owl-profile/QL` | PerfectRef rewriting | #95's part 3 |

   - **Matching.** Tokens are matched case-sensitively. An IRI names the
     regime of its row, and for the two OWL regimes the profile is the
     token's.
   - **Anything else** — an unknown value, a regime not yet available, more
     than one value — is `400 entailment-unsupported`, with members `value`
     and `supported`, the tokens this server serves.
   - **A query a regime refuses** — its `QueryRequestRefused` — is `400
     entailment-refused`, with member `reason`. No tier-1 regime refuses a
     query, so this part catalogues it and emits it nowhere.
2. **The default.**
   - Without the header, a query is evaluated under the dataset's default.
     That is the `varve:defaultEntailment` of its TBox bindings at the
     management position the read is served under (ADR 0194), and `simple`
     when there is none.
   - A request asks for simple entailment explicitly with
     `Varve-Entailment: simple`.
3. **The response.**
   - It carries `Varve-Entailment`, naming the regime applied as its token.
   - It carries `Vary: Varve-Entailment`. The header joins the `Vary` list
     every dataset response carries (ADR 0119), problems included.
   - The query result's `ETag` is per position and regime.
4. **Where the header applies.**
   - **Queries** honour it, on the SPARQL Protocol endpoints.
   - **An update request** with any value other than `simple` is
     `entailment-unsupported`. Its `WHERE` is evaluated under simple
     entailment, as SPARQL 1.1 Entailment Regimes §11 leaves to the
     implementation to state.
   - **Every other route** ignores the header, the Graph Store included,
     since a graph's representation is its asserted quads.
5. **The service description.**
   - **On the service:**
     - `sd:defaultEntailmentRegime` is the dataset's default (point 2);
     - `sd:defaultSupportedEntailmentProfile` is added only when that default
       is an OWL regime;
     - `sd:feature varve:EntailmentHeader` says the header is understood.
   - **On each `sd:NamedGraph` a TBox binding covers:** one
     `sd:entailmentRegime` per regime the server serves for it, and
     `sd:supportedEntailmentProfile` for the OWL ones.
   - **On a graph no binding covers:** nothing is written. The tier-1 regimes
     serve it all the same, over its own triples, since its TBox is empty.
   - **The default graph** has no `sd:NamedGraph` node, and the specification
     says nothing about a per-default-graph regime, so nothing is written
     there beyond the service's default.
6. **CORS (ADR 0132 amended).** `Varve-Entailment` joins the allowed request
   headers and the exposed response headers.
7. **The client and the command line.**
   - `Varve.Protocol.Client`'s query methods take an optional regime.
   - `varve query` takes `--entailment <token>`.

## Alternatives considered

- **A query-string parameter, `entailment=`.** It is linkable, but no other
  Varve read option is a parameter. `Varve-As-Of` set the precedent, and a
  parameter would be read by every SPARQL Protocol client as part of the
  query.
- **A pragma in the query text**, as some stores use. The query would not be
  standard SPARQL, and the parser would gain a dialect.
- **Selection by dataset only,** through the binding. The default is set that
  way, and the header still lets one request choose.
- **`sd:entailmentRegime` on every named graph.** That is one triple per
  regime per graph, all identical for unbound graphs. The feature term and
  the service default say the same once.
- **Refusing the header on the Graph Store.** It would make a client that
  sets it globally fail on unrelated routes. Ignoring it is what `Vary` and
  the echoed header make visible.

## Consequences

- One request header, one problem type with a page in `docs/problems/`, and
  two lines in each CORS list.
- The service description's `ETag` is already per position and per caller
  (ADR 0107). Its regime triples follow the management position like its
  graph list.

## Checks

- **Checked against the accepted ADRs** (0001–0178, 0190, 0191 and 0193).
  Touches:
  - **0092**: a new problem type;
  - **0096**: the header family;
  - **0105**: the command line's flag;
  - **0119** and **0132**: amended;
  - **0165**: the management position decides the default.

  No conflict.
- **Layer ownership.** `Varve.Protocol` (5) holds the header, the description
  and the problem. `Varve.Protocol.Client` (5) and `Varve.Server` (6) hold the
  client's and the command line's options.
- **Analyzer rule.** None.
- **Open questions owned.** None.
