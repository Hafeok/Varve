# Status fixture

## Status

**`v0.1.0-preview.2` is the latest release, and it closed milestone 7 ([#11](https://github.com/Hafeok/Varve/issues/11)).**

| Package | Layer | What it is | State |
|---|---:|---|---|
| `Varve.Iri` | 0 | IRI parsing, resolution and normalisation over UTF-8 | working |
| `Varve.Xsd` | 0 | the XSD 1.1 value spaces with SPARQL operator semantics: exact decimal, the integer family, IEEE double and float, boolean, string, the date and time family, durations | working |
| `Varve.Rdf` | 1 | terms, triples, quads, and the abstract quad source contract — with cardinality estimates and an inline-value accessor | working |
| `Varve.Turtle` | 2 | N-Triples, N-Quads, Turtle, TriG — reader and writer, RDF 1.2 throughout | working |
| `Varve.RdfXml` | 2 | RDF/XML over `System.Xml`'s reader and writer, with RDF 1.2's triple terms, annotations and directions | working |
| `Varve.JsonLd` | 2 | JSON-LD 1.1 over `Utf8JsonReader`: expansion, toRdf and fromRdf, `rdf:JSON` in canonical form | working |
| `Varve.Sparql` | 2 | SPARQL 1.1 Query and Update as an immutable algebra, with the 1.2 additions; the parser and the serialiser | working |
| `Varve.Sparql.Results` | 2 | the SPARQL results formats — XML, JSON, CSV, TSV — as pull readers and streaming writers | working |
| `Varve.Sparql.Evaluation` | 3 | the optimiser and evaluator: every operator, the function library, aggregates, property paths, `SERVICE` through a handler, over any quad source | working |
| `Varve.Aspire` | 0 | the .NET Aspire hosting integration: `AddVarve`, `WithOidc`, `WithDataset` on an AppHost; references nothing from Varve | working |
| `Varve.Analyzers` | — | the layer rules, at build time | working, never shipped |
| `Varve.Store` | 4 | the log, the default projection, pinned and as-of reads, checkpoints, subscriptions, dataset validators, the staging view, the bulk loader, replicas by file copy; `log/` format 1 on files, `derived/` format 3 | working |
| `Varve.Sparql.Store` | 5 | SPARQL Update over the store: one request, one commit, `LOAD` through a caller's source, read and write scopes by graph | working |
| `Varve.Store.Browser` | 5 | the store in a browser: the origin private file system through synchronous access handles in a worker, or IndexedDB | working |
| `Varve.Protocol` | 5 | the SPARQL 1.1 Protocol, the Graph Store Protocol with graphs in six formats, the service description, time travel by `Varve-As-Of`, the change feed and the diff, the admin API, and graph-level authorisation, on any ASP.NET Core host | working |
| `Varve.Protocol.Client` | 5 | the HTTP client of a dataset, `SERVICE` over HTTP for the evaluator and the document fetch `LOAD` needs, under an endpoint policy | working |
| `Varve.Server` | 6 | the executable: the server with OIDC bearer tokens, health and readiness, telemetry, governed limits, and the `varve` command line — create, info, load, query, update, export, checkpoint, feed, lease, serve | working, Native AOT, container image |
| SHACL | 3 | | not built |

### Conformance

| Suite | Passing |
|---|---:|
| a fixture suite | 3 |
| **Total** | **3 of 3** |

## Quick start

```bash
docker pull ghcr.io/hafeok/varve:v0.1.0-preview.2
docker run --rm -p 8080:8080 ghcr.io/hafeok/varve:v0.1.0-preview.1 serve --anonymous --dataset people
```
