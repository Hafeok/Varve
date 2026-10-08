---
set: service-and-load-over-http
namespace: varve
adr: 0104
decisions:
  - key: HttpServiceHandlerSendsSelectStar
    statement: "HttpServiceHandler implements IServiceHandler by POSTing SELECT * WHERE { P } serialised with SparqlWriter as application/sparql-query, after the endpoint policy has allowed the endpoint"
  - key: ServiceResultsNegotiatedJsonThenXml
    statement: "The handler asks for SPARQL results JSON then XML, parses the answer with SparqlResultsReader, and treats any other media type, status, a body over the cap or a timeout as a failure returned with the endpoint and the reason"
  - key: ServiceSilentIsTheEvaluators
    statement: "The handler never consults SILENT: it returns failures and the evaluator errors or answers one empty solution per ADR 0055; incoming solutions are not pushed to the endpoint"
  - key: LoadDocumentFetchedByClientHostBindsSource
    statement: "RdfDocumentClient fetches an RDF document by IRI with content negotiation over the four syntaxes, the base the request IRI, the bytes capped, the policy consulted first; each host binds ILoadSource to it in one line"
  - key: FederationAndLoadConfigurationSections
    statement: "The server's Federation and Load sections each hold one policy (AllowedEndpoints or AllowedSources, AllowPrivateAddresses) and one set of limits (Timeout, MaxResponseBytes); with neither, the refusing defaults stay"
  - key: ServiceSuiteRunsOverHttp
    statement: "sparql11/service runs end to end over HTTP against one in-process server per qt:serviceData endpoint, through HttpServiceHandler with the manifests' endpoint IRIs allowed and mapped to loopback by a test-side handler, beside its in-process run"
---

The rulings of [ADR 0104](../adr/0104-service-and-load-over-http.md), filed unaccepted by milestone 7b of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
