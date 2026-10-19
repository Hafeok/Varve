# Varve

A graph database and RDF/SPARQL toolkit for .NET, event-sourced from the first
commit.

The name is geological: a varve is one annual sediment layer, countable and
datable. One commit is one varve. The vocabulary stays in the documentation —
public API names are conventional (`Commit`, `Position`, `Projection`).

## The storage thesis

**The transaction log is the source of truth. Every index is a projection of
it.**

Most graph stores keep indexes as the truth and bolt history on afterwards, as
a changelog, an audit table, or a temporal extension. Varve inverts that. The
log records **what changed, not what was asked for** — the effective delta of
each commit — and it is **never rewritten**. Indexes, checkpoints and
projections are derived, and any of them can be dropped and rebuilt without
losing anything.

Three properties fall out of the model rather than being features added to it:

- **Snapshot isolation.** A read is pinned to a log position, so it sees one
  consistent state for its whole lifetime without locking anything.
- **Time travel.** An as-of read is the nearest checkpoint plus a delta
  overlay, at any closed position. There is **no compaction** in the
  destructive sense, so history does not expire.
- **Change feeds.** A projection is a fold over the log. Subscribing to changes
  is the same mechanism that builds the indexes.

The cost is stated plainly: writes go through one sequencer, and the log grows.
Checkpoints bound read cost; nothing bounds the log, by design.

There is one deliberate exception to "never rewritten", for a reason no
append-only store can otherwise answer. **Erasure mode**, off by default per
dataset, encrypts personal terms under a per-subject key, and erasure destroys
the key — the log is untouched and the data is gone. Keys never live in the
dataset directory.

What that buys a system of record, against the stack it usually takes, and
where Varve is the wrong tool: [`docs/why.md`](docs/why.md).

## Status

**`v0.1.0-preview.9` is the latest release, and it completed the first part of milestone 8, SHACL ([#120](https://github.com/Hafeok/Varve/issues/120), under [#13](https://github.com/Hafeok/Varve/issues/13))**:
the standalone validator, SHACL 1.2 Core and SHACL-SPARQL over any quad
source, recursive shapes as a stratified greatest fixpoint, the report as a
canonical value that joins back to the data, XPath regular expressions in
`Varve.Xsd` for SHACL and the evaluator alike, and shapes for the event
declaration vocabulary. Commit-time gating and incremental validation are
part 2. `v0.1.0-preview.8` before it closed milestone Object storage
([#92](https://github.com/Hafeok/Varve/issues/92)): the log and the
projection in Azure Blob Storage or an S3-compatible store, one writer by an
epoch, checkpoints as extent sets with validity intervals, and a deployment on
Azure Container Apps. `v0.1.0-preview.7` before that closed milestone Client
and integration ([#93](https://github.com/Hafeok/Varve/issues/93)) with its
second part: domain events written as quads and rendered as canonical
CloudEvents, their catalogue as graphs governed by the graphs they describe,
versioned JSON Schemas with breaking changes refused, AsyncAPI 3.0,
`FollowEventsAsync`, and an Event Hubs bridge sample. `v0.1.0-preview.6` before
that closed the first part
([#103](https://github.com/Hafeok/Varve/issues/103)): on-behalf-of through a
token provider, the change feed's format as the package `Varve.Delta`,
`FollowAsync`, declared dataset setup and CORS.
`v0.1.0-preview.5` before that closed milestone Commit path
([#91](https://github.com/Hafeok/Varve/issues/91)): concurrent commits written
together in one flush, and writers that conflict only over the graphs they
read or write, with graph versions as HTTP tags.
`v0.1.0-preview.4` before that closed milestone 6
([#10](https://github.com/Hafeok/Varve/issues/10)) with its last slice, 6b:
RDF 1.2 Turtle and TriG, RDF/XML and JSON-LD, read, written and negotiated
over HTTP. `v0.1.0-preview.3` before that closed
milestone Operability ([#12](https://github.com/Hafeok/Varve/issues/12)): the
server somebody can run. Twenty-four packages and one executable: reading and
writing seven syntaxes, canonical JSON, the XSD value spaces, RDFC-1.0
canonicalisation, the SPARQL algebra with its parser and serialiser, the
SPARQL results formats read and written, a query evaluator with an optimiser,
a SHACL validator with SHACL-SPARQL,
an event-sourced store on files, in memory, in the browser and in an object
store, SPARQL Update
over it as one commit per request, the SPARQL 1.1 and Graph Store protocols
with a change feed over HTTP and its format, domain events as CloudEvents
with their catalogue and schemas, an HTTP client with `SERVICE`, `LOAD`,
following and setup, and
`Varve.Server`: the server, with OIDC and graph-level grants, health and
readiness, telemetry over OTLP, a stated answer for every limit, and the
`varve` command line, as a .NET tool, a Native AOT single file and the
container image `ghcr.io/hafeok/varve`; and `Varve.Aspire`, the AppHost
integration. `docs/operator/` says how to run it.

This section is checked (`eng/status.cs`): its first line names the latest
release, the tables below match the projects and the conformance baseline,
and it ships inside every package.

| Package | Layer | What it is | State |
|---|---:|---|---|
| `Varve.Iri` | 0 | IRI parsing, resolution and normalisation over UTF-8 | working |
| `Varve.Json.Canonical` | 0 | RFC 8785 canonical JSON over UTF-8: string escaping, ECMAScript numbers, member order, and a writer that cannot write anything else; the one implementation `Varve.JsonLd` and `Varve.Events` share | working |
| `Varve.Xsd` | 0 | the XSD 1.1 value spaces with SPARQL operator semantics: exact decimal, the integer family, IEEE double and float, boolean, string, the date and time family, durations | working |
| `Varve.Rdf` | 1 | terms, triples, quads, and the abstract quad source contract — with cardinality estimates and an inline-value accessor | working |
| `Varve.Turtle` | 2 | N-Triples, N-Quads, Turtle, TriG — reader and writer, RDF 1.2 throughout | working |
| `Varve.RdfXml` | 2 | RDF/XML over `System.Xml`'s reader and writer, with RDF 1.2's triple terms, annotations and directions | working |
| `Varve.JsonLd` | 2 | JSON-LD 1.1 over `Utf8JsonReader`: expansion, toRdf and fromRdf, `rdf:JSON` in canonical form | working |
| `Varve.Sparql` | 2 | SPARQL 1.1 Query and Update as an immutable algebra, with the 1.2 additions; the parser and the serialiser | working |
| `Varve.Sparql.Results` | 2 | the SPARQL results formats — XML, JSON, CSV, TSV — as pull readers and streaming writers | working |
| `Varve.Delta` | 3 | the change feed's format, `application/vnd.varve.delta` version 1, read for ever: the reader, the writer and the record model, with no store in it | working |
| `Varve.Sparql.Evaluation` | 3 | the optimiser and evaluator: every operator, the function library, aggregates, property paths, `SERVICE` through a handler, over any quad source | working |
| `Varve.Shacl` | 3 | SHACL 1.2 Core over any quad source: targets, paths, every core component, recursive shapes by stratified greatest fixpoint, the validation report as a canonical value, and an extension contract | working |
| `Varve.Aspire` | 0 | the .NET Aspire hosting integration: `AddVarve`, `WithOidc`, `WithDataset` on an AppHost; references nothing from Varve | working |
| `Varve.Analyzers` | — | the layer rules, at build time | working, never shipped |
| `Varve.Events` | 4 | domain events as quads: the event catalogue's declarations, their rendering as canonical CloudEvents through declared `SELECT` mappings, JSON Schemas with immutable revisions, AsyncAPI 3.0, the compatibility check of a declaration change, and the declaration vocabulary's SHACL shapes | working |
| `Varve.Shacl.Sparql` | 4 | SHACL-SPARQL: SPARQL-based constraints and constraint components, evaluated by `Varve.Sparql.Evaluation` with SHACL 1.2's pre-binding | working |
| `Varve.Store` | 4 | the log, the default projection, pinned and as-of reads, checkpoints, subscriptions, dataset validators, the staging view, the bulk loader, replicas by file copy; checkpoints as extent sets with validity intervals; `log/` format 1, `derived/` format 4, reading format 3 | working |
| `Varve.Sparql.Store` | 5 | SPARQL Update over the store: one request, one commit, `LOAD` through a caller's source, read and write scopes by graph | working |
| `Varve.Store.Browser` | 5 | the store in a browser: the origin private file system through synchronous access handles in a worker, or IndexedDB | working |
| `Varve.Store.ObjectStorage` | 5 | the store in any object store with conditional writes: the log as write-once chunk objects, one writer by an epoch fenced in the log, a local cache | working |
| `Varve.Store.Azure` | 5 | Azure Blob Storage for the object-storage backend, over the REST API, authenticated by the workload's identity | working |
| `Varve.Store.S3` | 5 | Amazon S3 and S3-compatible servers for the object-storage backend, with conditional writes, SigV4 from the workload's web identity | working |
| `Varve.Protocol` | 5 | the SPARQL 1.1 Protocol, the Graph Store Protocol with graphs in six formats, the service description, time travel by `Varve-As-Of`, the change feed and the diff, the admin API, and graph-level authorisation, on any ASP.NET Core host | working |
| `Varve.Protocol.Client` | 5 | the HTTP client of a dataset, `SERVICE` over HTTP for the evaluator and the document fetch `LOAD` needs, under an endpoint policy | working |
| `Varve.Server` | 6 | the executable: the server with OIDC bearer tokens, health and readiness, telemetry, governed limits, and the `varve` command line — create, info, load, query, update, export, checkpoint, feed, lease, serve | working, Native AOT, container image |

### Conformance

The W3C suites are the acceptance gate, from
`tests/Varve.Conformance.Tests/baseline/passing.txt`:

| Suite | Passing |
|---|---:|
| `rdf11/rdf-trig` | 357 |
| `rdf11/rdf-turtle` | 313 |
| `rdf11/rdf-n-quads` | 87 |
| `rdf11/rdf-n-triples` | 70 |
| `rdf12/rdf-n-triples` (syntax) | 29 |
| `rdf12/rdf-n-quads` (syntax) | 27 |
| `rdf12/rdf-n-triples`, `rdf-n-quads` (canonical form, ADR 0061) | 82 |
| `rdf12/rdf-turtle`, `rdf-trig` (syntax and evaluation, ADR 0121) | 167 |
| `rdf11/rdf-xml` | 166 |
| `rdf12/rdf-xml` (evaluation) | 31 |
| `json-ld-api/toRdf` (448 of 467; the rest 1.0-only or generalized RDF, by rule) | 448 |
| `json-ld-api/expand` (368 of 386, likewise) | 368 |
| `json-ld-api/fromRdf` (53 of 54, likewise) | 53 |
| `sparql10/syntax-sparql1` … `5` (parsed under 1.1) | 199 |
| `sparql11/syntax-query` | 94 |
| `sparql11/syntax-update-1`, `-2`, `syntax-fed` | 58 |
| `sparql12/syntax-triple-terms-positive`, `-negative` | 178 |
| `sparql12/syntax`, `version`, `codepoint-escapes`, `lang-basedir` | 25 |
| `sparql10` query evaluation, 24 directories (under 1.1), × 4 subjects | 1,132 |
| `sparql11` query evaluation, 15 directories with `service`, × 4 subjects | 968 |
| `sparql11/service` with the endpoints over HTTP (ADR 0104) | 7 |
| `sparql12` query evaluation, 6 directories, × 4 subjects | 205 |
| `sparql11/csv-tsv-res`, `json-res`, written by the results writers | 10 |
| `sparql11` update evaluation, 11 directories, one commit or none each, in process and through the protocol | 188 |
| `sparql11/protocol`, `service-description`, over both stores | 74 |
| `sparql11/http-rdf-update`, over both stores, 6 justified exemptions each | 50 |
| `rdf-canon`, RDFC-1.0: 64 canonical forms, 21 issued maps, 1 refusal | 86 |
| `data-shapes-test-suite`, SHACL 2017, `core` and `sparql`, × 2 subjects | 232 |
| `shacl12-test-suite`, SHACL 1.2 Working Draft, `core` and `sparql`, × 2 subjects | 390 |
| **Total** | **6,141 of 6,141** |

Each SPARQL suite is parsed at its own version, so the 1.0 and 1.1 suites
never see the 1.2 grammar; the 1.2 default is for API callers only. The six
SPARQL 1.2 suites are against Working Drafts of 2026, ratcheted anyway
because the algebra carries 1.2 from the start and the ratchet is what
absorbs churn (`docs/spec/sparql-grammar.md` §1).

Each query evaluation case runs over four subjects — `InMemoryDataset`, the
store's pinned view, that view through a graph scope naming every graph of
the case (ADR 0107), and the protocol over HTTP with an `all` grant — and is
one ratchet line per subject; a guard runs every case again in ADR 0050's two
other value-access arms. The 41 SPARQL 1.2 evaluation cases whose data is RDF
1.2 Turtle or TriG run since milestone 6b (ADR 0121), and all 41 pass over
every subject since the store answers a triple term's components by handle
([#87](https://github.com/Hafeok/Varve/issues/87)).

**`baseline/exemptions.txt` holds forty exemptions.** Twelve are in the
deprecated `http-rdf-update` suite, six a store, each with the section it
rests on: two bodies in Turtle with no final `.` (Turtle 1.1 §2.4), the two
`GET`s that depend on them, a `DELETE` of a graph no step creates (Graph Store
Protocol §5.4), and a `HEAD` without `Accept` that expects Turtle (§5.2 allows
N-Triples). The twelve SPARQL 1.2 `eval-triple-terms` exemptions over the
store, its graph scope and the protocol left with
[#87](https://github.com/Hafeok/Varve/issues/87): the store answers
`IQuadSource.TryGetTripleTermComponents` from its dictionary (ADR 0121,
amended 2026-10-15), so a blank node inside a triple term keeps the identity
ADR 0044 gives it. No
syntax, canonicalisation or update case needs one, including none for empty
graphs, which the store does not record (SPARQL 1.1 Update §3.2 allows it).
The other twenty-eight are fourteen SHACL-SPARQL cases, over both subjects,
each with its reason under ADR 0149: four where SHACL 2017's pre-binding rules
and the 1.2 draft's disagree, and 1.2 is followed; and ten that use
SPARQL-based node expressions, which part 1 leaves out beyond constants. Every
SHACL Core case passes, 2017's and 1.2's. Each SHACL case runs over
`InMemoryDataset` and over the store's pinned view, with SHACL-SPARQL on.
`eng/ratchet.cs` fails the build if any of those 6,141 stops passing, and an
exemption with no written justification fails the run too.

Also true today, and measured rather than asserted:

- **Zero bytes allocated per quad**, on every entry point of N-Triples,
  N-Quads, Turtle and TriG — measured as the difference between a 500-quad
  and a 4,000-quad parse, because an absolute figure measures the harness as
  much as the parser. RDF/XML costs what `XmlReader` costs, 16 bytes a
  triple, stated rather than hidden (ADR 0122); JSON-LD is a tree before it
  is a dataset, zero per quad in a steady state and the tree's growth on the
  first document of a size, and at most 533 bytes a quad to write (ADR 0123).
- **Native AOT and browser WebAssembly** both read and write Turtle and TriG
  with RDF 1.2, RDF/XML and JSON-LD. CI publishes the AOT binary and *runs*
  it; the interesting AOT failures are at run time and silent.
- **Every reader runs the chunk-boundary oracle**: parse whole, parse again
  split at every byte offset, require the same answer. It has found nine defect
  classes, two of which produced *wrong quads rather than errors*.

- **The store's specification is tested against a reference model.**
  §10's properties — effective deltas, dictionary closure, monotone time, the
  header chain, checkpoint and rebuild equivalence, pinned and as-of reads,
  `Diff`, crash recovery at every byte offset, byte-identical logs — run as
  CsCheck properties against a naive fold written from the specification
  alone. A scan allocates zero bytes per quad; AOT and the browser both run
  the store.
- **The SPARQL parser allocates the tree and nothing else**: parsing 350 more
  triple patterns allocates exactly what building them by hand allocates.
  **Parse, write, parse gives the identical tree**, by record equality, over
  3,000 generated queries and 3,000 generated updates per run and over every
  positive corpus case; and every corpus case parsed as UTF-16 agrees with
  its UTF-8 parse. AOT and the browser both parse a query and print its
  algebra.
- **The optimiser changes no answer**: 20,000 generated queries over
  generated data per run give the same multiset optimised and not, and every
  rewrite must have fired. Its one counterexample was an operator whose answer
  depended on join order, now fixed. **The store answers as a dataset of its
  own quads**: at a generated as-of position of a generated history with
  retractions, 2,000 times a run. A `SELECT` over a basic graph pattern
  allocates **its 48-byte row per solution, and nothing per quad** scanned and
  not matched, over the store and over `InMemoryDataset`. AOT and the browser
  both evaluate a basic graph pattern, an aggregate and a property path.
- **The store's cardinality estimate is exact**, held to a counted scan over
  generated histories at the head and at every as-of position (ADR 0049).
- **One update request is one commit.** Every update case asserts exactly one
  commit when the request's net effect is non-empty and none otherwise, and a
  property applies 2,000 generated request sequences a run both through
  `Varve.Sparql.Store` and as a term-level model's deltas through the commit
  API, and requires the same state at every position and the same commits.
- **Results written read back as written**: 5,000 generated solution
  sequences per format a run, with blank nodes, directional literals, triple
  terms and unbound variables, through the writer and back through the
  reader — term for term for XML, JSON and TSV, and what CSV carries for CSV.
- **Isomorphic datasets canonicalise alike, and only they do**: 20,000
  generated pairs a run against the harness's backtracking check, plus
  idempotence and a read-back property. It found that **RDFC-1.0 itself gives
  some isomorphic datasets with blank graph names different canonical forms**
  — pyoxigraph gives the same two forms — which `docs/spec/rdf-canon.md` §6
  states; with IRI graph names the equivalence holds.

**RDF 1.2 is accepted in every syntax.** Turtle and TriG read and write
reified triples, triple terms, annotations, reifiers, the version directive
and directional language tags (ADR 0121); `docs/spec/turtle.md` §9 says how
the two editions' one contradiction is decided. SPARQL 1.2 is accepted in
full, triple terms, reifiers, annotations and `VERSION` included, because the
algebra was built with 1.2 from the start.

**Queries evaluate and updates commit.** `Varve.Sparql.Evaluation` answers
`SELECT`, `ASK`, `CONSTRUCT` and `DESCRIBE` over any quad source, and
`Varve.Sparql.Store` executes an update request against a `Dataset` as one
commit. `LOAD` reads through a source the caller supplies, and the server
binds it, and `SERVICE`, to HTTP under an allow-list (ADR 0104). `NOW()` and the
random functions read a clock and a random source the caller supplies —
`Clock = TimeProvider.System` for the system clock — and fail by the option's
name without one (ADR 0056).

**Every release is on nuget.org**, published through trusted publishing,
and since `v0.1.0-preview.3` the server is also the container image
`ghcr.io/hafeok/varve`, with its build provenance and an SPDX SBOM attested
(`gh attestation verify`, `docs/operator/run.md`). A release is a descriptor
in `releases/`, cut when its pull request lands (`docs/releases.md`,
ADR 0102).

## Quick start

### Embedded

```bash
dotnet add package Varve.Turtle
```

```csharp
await foreach (Quad quad in TurtleReader.ReadAsync(stream, baseIri))
{
    // The reader is streaming and allocation-free per quad. The quad is a
    // view; copy anything you intend to keep past the next iteration.
}
```

`Varve.Rdf` gives you the term model and the quad source contract if you only
want those; `Varve.Iri` is usable on its own wherever `System.Uri` is the wrong
answer, which for an IRI it is.

```csharp
Query query = SparqlParser.ParseQuery("SELECT ?s WHERE { ?s a ?type } LIMIT 10"u8);
string algebra = SparqlWriter.ToText(query);   // parses back to the identical tree
```

`Varve.Sparql` parses SPARQL 1.1 and 1.2 Query and Update into an immutable
algebra with a source span on every node, and `Varve.Xsd` gives the value
spaces an evaluator compares with — `XsdDecimal`, `XsdDateTime` and the rest,
allocation-free.

### Browser (WebAssembly)

The same packages, unchanged. They target `net10.0` with no native assets and
no reflection, so `browser-wasm` needs nothing special —
`tests/Varve.WasmSmoke` is a working example, and it is what proved that no
symmetric cipher exists in that host, which changed a decision (ADR 0028).

### Server

`varve` is the server and the command line in one executable. Take it as a
.NET tool, which needs the .NET 10 runtime, or as the container image, which
is the Native AOT binary and nothing else:

```bash
dotnet tool install --global Varve.Server --prerelease
docker pull ghcr.io/hafeok/varve:v0.1.0-preview.9
```

Serve one dataset on your machine. `--anonymous` is for development only: a
real deployment takes OIDC bearer tokens ([ADR 0037](docs/adr/0037-server-authentication.md)).

```bash
varve serve --anonymous --datasets-root "$PWD/data" --dataset people
# or, listening on 8080 instead of 5000, with the datasets in the image's volume:
docker run --rm -p 8080:8080 ghcr.io/hafeok/varve:v0.1.0-preview.9 serve --anonymous --dataset people
```

The datasets root is an absolute path. Then write, read, go back, and read
the history:

```bash
curl -X POST http://localhost:5000/datasets/people/sparql \
  -H 'Content-Type: application/sparql-update' \
  --data 'INSERT DATA { <http://example.org/ada> <http://xmlns.com/foaf/0.1/name> "Ada Lovelace" }'
curl -X POST http://localhost:5000/datasets/people/sparql \
  -H 'Content-Type: application/sparql-update' \
  --data 'DELETE DATA { <http://example.org/ada> <http://xmlns.com/foaf/0.1/name> "Ada Lovelace" }'

# No rows: commit 2 deleted her.
curl -G http://localhost:5000/datasets/people/sparql \
  -H 'Accept: application/sparql-results+json' \
  --data-urlencode 'query=SELECT ?name WHERE { ?person <http://xmlns.com/foaf/0.1/name> ?name }'

# "Ada Lovelace": the dataset as commit 1 left it.
curl -G http://localhost:5000/datasets/people/sparql \
  -H 'Accept: application/sparql-results+json' -H 'Varve-As-Of: position:1' \
  --data-urlencode 'query=SELECT ?name WHERE { ?person <http://xmlns.com/foaf/0.1/name> ?name }'

# Both commits, as the changes they made. Without `to` the feed stays open
# and tails new commits as they close.
curl 'http://localhost:5000/datasets/people/commits?to=2'
```

[`docs/operator/`](docs/operator/README.md) is the rest: running it under
systemd or a container runtime, every configuration key, OIDC with Entra ID
or any other issuer, health, telemetry, backup and upgrade.
[`Varve.Aspire`](src/Varve.Aspire/README.md) puts the same container in a
.NET Aspire AppHost.

## Building

```bash
git clone --recurse-submodules https://github.com/Hafeok/Varve
dotnet run eng/ci.cs        # the whole pipeline, exactly as CI runs it
```

Open the repository in the devcontainer and everything the gates need is
already there. `dotnet run eng/ci.cs -- --list` says what the pipeline
consists of. [`CONTRIBUTING.md`](CONTRIBUTING.md) has the rest.

## Constraints

1. 100% managed code. No P/Invoke, no native assets.
2. Native AOT and trimming compatible.
3. One core, three hosts: embedded library, server, browser (WASM).
4. Minimal dependencies. Every third-party package has an ADR.
5. Current LTS .NET and current C#. Allocation per quad is a defect.
6. No copied code. **The "permissive licence" half of this constraint is
   superseded** — Varve is MPL-2.0, which is copyleft. See
   [ADR 0031](docs/adr/0031-licence-mpl-2-0.md).

`docs/brief.md` is the authority for all of the above and is not summarised
accurately anywhere else, including here.

## Documentation

- [`docs/why.md`](docs/why.md) — why Varve: what it replaces, and where it
  is the wrong tool.
- [`docs/brief.md`](docs/brief.md) — the project brief. The authority.
- [`docs/adr/`](docs/adr/) — architecture decisions. Numbered, superseded
  rather than edited.
- [`docs/spec/`](docs/spec/) — functional specifications, per component.
- [`docs/operator/`](docs/operator/README.md) — running the server: install,
  configure, authenticate, observe, back up, upgrade.
- [`docs/problems/`](docs/problems/README.md) and
  [`docs/vocabulary.md`](docs/vocabulary.md) — every problem type the server
  answers, and every term under `https://w3id.org/varve/ns#`.
- [`docs/rules/`](docs/rules/) — one page per `VARVE` analyzer rule.
- [`docs/roadmap.md`](docs/roadmap.md) — milestones, the 1.0 definition, and
  what is deferred.
- [`docs/testing.md`](docs/testing.md) — what each kind of test is for.
- [`docs/traceability/`](docs/traceability/) — one record per AI-assisted
  session.

| | |
|---|---|
| Contributing | [CONTRIBUTING.md](CONTRIBUTING.md) |
| Governance | [GOVERNANCE.md](GOVERNANCE.md) |
| Security | [SECURITY.md](SECURITY.md) |
| Conduct | [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) |
| Changes | [CHANGELOG.md](CHANGELOG.md) |
| Questions and ideas | [Discussions](https://github.com/Hafeok/Varve/discussions) |

## How this project is run

Varve is run to the **Mind Over Machine open-source stewardship standard**:
trunk-based development with non-blocking reviews, containerised development
and pipelines, full commit traceability, signed commits, Detroit-style testing,
version-controlled test data, and open project management.
[GOVERNANCE.md](GOVERNANCE.md) is what that means here in practice.

**Varve is developed with AI assistance under human review.** Every change
passes the same gates whoever wrote it — the build at zero warnings, the
conformance ratchet, the dependency register, the native-asset gate, the
licence-header check — and every commit carries a sign-off naming the person
accountable for it. Each AI-assisted session leaves a record in
[`docs/traceability/`](docs/traceability/) with the prompt it was given and the
report it returned.

## Licence

**[MPL-2.0](LICENSE).** See also [`NOTICE`](NOTICE) and
[ADR 0031](docs/adr/0031-licence-mpl-2-0.md), which supersedes the Apache-2.0
decision in [ADR 0002](docs/adr/0002-licence.md).

MPL-2.0 is **file-level** copyleft, and the distinction matters:

- Referencing a Varve package from your own code does **not** make your code
  copyleft. Build whatever you like on top, under whatever terms you like.
- Modifying a Varve source file means **that file** stays MPL-2.0 and its
  source has to remain available.

Copyright 2026 Emil Okkels Klein.
