# Varve.Protocol.Client

The HTTP client for a Varve server, and what a server needs to reach other
servers: `SERVICE` over HTTP for the evaluator, and the document fetch that
`LOAD` needs, both under an endpoint policy the operator sets.

- **`SparqlHttpClient`** talks to one dataset's endpoints: queries and updates
  (SPARQL 1.1 Protocol), the Graph Store Protocol, the change feed and the
  diff, and the admin API, with a bearer token, `If-Match`, `Varve-As-Of` and
  `Accept` as the caller says.
- **`HttpServiceHandler`** implements the evaluator's `IServiceHandler`: the
  `SERVICE` pattern is sent as `SELECT … WHERE { … }`, the answer read back
  with `Varve.Sparql.Results`. A failure is returned, and `SILENT` is the
  evaluator's decision (ADR 0055).
- **`RdfDocumentClient`** fetches an RDF document by IRI with content
  negotiation over the four syntaxes. A host binds `ILoadSource` to it.
- **`EndpointPolicy`** decides every outbound address before a connection is
  opened: an allow-list of IRI prefixes, empty by default; `http` and `https`
  only; loopback, link-local and private addresses refused unless allowed;
  no redirects, no credentials in the authority.
- **`ClientLimits`** bound every request: a timeout and a cap on response
  bytes.

```csharp
EndpointPolicy policy = new(["https://query.wikidata.org/"], allowPrivateAddresses: false);
HttpClient http = OutboundHttp.CreateClient();
EvaluationOptions options = new() { ServiceHandler = new HttpServiceHandler(http, policy, ClientLimits.Default) };
```

ADRs 0103 and 0104.
