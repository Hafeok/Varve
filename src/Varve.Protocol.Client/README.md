# Varve.Protocol.Client

The HTTP client for a Varve server, and what a server needs to reach other
servers: `SERVICE` over HTTP for the evaluator, and the document fetch that
`LOAD` needs, both under an endpoint policy the operator sets.

- **`SparqlHttpClient`** talks to one dataset's endpoints: queries and updates
  (SPARQL 1.1 Protocol), the Graph Store Protocol, the change feed and the
  diff, and the dataset's management resources under `/manage/` (with a token
  for the management audience), with `If-Match`, `Varve-As-Of` and `Accept` as the
  caller says. Its bearer token comes from a **`BearerTokenProvider`** called
  for every request, so one client serves many users; a token past its expiry
  is never sent (ADR 0127).
- **`FollowAsync`** follows the commits: history in pages, then the live tail,
  each commit to a handler and then its position to an `IFollowCheckpoint`:
  at least once, resumable from any position (ADR 0129). The records are
  `Varve.Delta`'s, so a consumer of the feed ships no store (ADR 0128).
- **`ApplyAsync`** makes a dataset's data what a **`DatasetSetup`**
  declares: its baseline by the Graph Store Protocol and its migrations, on a
  dataset the management plane declares (ADR 0163), each recorded in `varve:setup` in its own commit; applying it
  again commits nothing (ADR 0130). **`DatasetSetupFile`** reads one from the
  setup file's YAML subset.
- **`AddVarve`** (`Varve.Protocol.Client.Hosting`) registers the client in an
  application's services and applies its setup as the host starts, failing
  the start when it cannot (ADR 0131).
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

ADRs 0103, 0104 and 0127–0131.
