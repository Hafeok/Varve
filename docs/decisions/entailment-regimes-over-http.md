---
set: entailment-regimes-over-http
namespace: varve
adr: 0192
decisions:
  - key: EntailmentHeader
    statement: "A query selects its regime by the Varve-Entailment request header, one token of simple, rdf, rdfs, d, owl2-rl or owl2-ql or one regime IRI in angle brackets, and anything else or a regime not yet served is 400 entailment-unsupported naming the value and the supported tokens"
  - key: DefaultFromTheBinding
    statement: "Without the header a query is evaluated under the dataset's default, the varve:defaultEntailment of its TBox bindings at the management position the read is served under, and simple entailment without one"
  - key: RegimeEchoedAndVaried
    statement: "A query's response names the regime applied in Varve-Entailment, carries Vary: Varve-Entailment, and its ETag is per position and regime"
  - key: UpdatesAreSimple
    statement: "An update request with a regime other than simple is entailment-unsupported, its WHERE evaluated under simple entailment, and every route but the query endpoints ignores the header"
  - key: ServiceDescriptionAdvertises
    statement: "The service description gives the dataset's default as sd:defaultEntailmentRegime, the feature varve:EntailmentHeader, and on each named graph a TBox binding covers one sd:entailmentRegime per regime served for it, with sd:supportedEntailmentProfile for the OWL ones"
  - key: EntailmentHeaderInCors
    statement: "Varve-Entailment is an allowed request header and an exposed response header under CORS (ADR 0132, amended)"
---

The rulings of [ADR 0192](../adr/0192-entailment-regimes-over-http.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 1 (#170) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
