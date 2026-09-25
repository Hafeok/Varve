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

## Status

**Milestone 5b.** Eight packages: reading and writing four syntaxes, the XSD
value spaces, the SPARQL algebra with its parser and serialiser, the SPARQL
results formats, a query evaluator with an optimiser, and an event-sourced
store in memory.

| Package | Layer | What it is | State |
|---|---:|---|---|
| `Varve.Iri` | 0 | IRI parsing, resolution and normalisation over UTF-8 | working |
| `Varve.Xsd` | 0 | the XSD 1.1 value spaces with SPARQL operator semantics: exact decimal, the integer family, IEEE double and float, boolean, string, the date and time family, durations | working |
| `Varve.Rdf` | 1 | terms, triples, quads, and the abstract quad source contract — with cardinality estimates and an inline-value accessor | working |
| `Varve.Turtle` | 2 | N-Triples, N-Quads, Turtle, TriG — reader and writer | working |
| `Varve.Sparql` | 2 | SPARQL 1.1 Query and Update as an immutable algebra, with the 1.2 additions; the parser and the serialiser | working |
| `Varve.Sparql.Results` | 2 | the SPARQL results formats — XML, JSON, CSV, TSV — as pull readers | working; writers at 5c |
| `Varve.Sparql.Evaluation` | 3 | the optimiser and evaluator: every operator, the function library, aggregates, property paths, `SERVICE` through a handler, over any quad source | working; update execution at 5c |
| `Varve.Analyzers` | — | the layer rules, at build time | working, never shipped |
| `Varve.Store` | 4 | the log, the default projection, pinned and as-of reads, checkpoints, subscriptions | working, in memory; file and browser backends at milestone 6 |
| SHACL, server, CLI | 3–5 | | not built |

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
| `sparql10/syntax-sparql1` … `5` (parsed under 1.1) | 199 |
| `sparql11/syntax-query` | 94 |
| `sparql11/syntax-update-1`, `-2`, `syntax-fed` | 58 |
| `sparql12/syntax-triple-terms-positive`, `-negative` | 178 |
| `sparql12/syntax`, `version`, `codepoint-escapes`, `lang-basedir` | 25 |
| `sparql10` query evaluation, 24 directories (under 1.1), × 2 subjects | 566 |
| `sparql11` query evaluation, 15 directories with `service`, × 2 subjects | 478 |
| `sparql12` query evaluation, 6 directories, × 2 subjects | 44 |
| **Total** | **2,525 of 2,525** |

Each SPARQL suite is parsed at its own version, so the 1.0 and 1.1 suites
never see the 1.2 grammar; the 1.2 default is for API callers only. The six
SPARQL 1.2 suites are against Working Drafts of 2026, ratcheted anyway
because the algebra carries 1.2 from the start and the ratchet is what
absorbs churn (`docs/spec/sparql-grammar.md` §1).

Each query evaluation case runs over two subjects, `InMemoryDataset` and the
store's pinned view, and is one ratchet line per subject; a guard runs every
case again in ADR 0050's two other value-access arms. **41 SPARQL 1.2
evaluation cases are blocked**, not exempt: their data is RDF 1.2 Turtle,
which `turtle.md` §9 refuses, and roadmap slice 6b unblocks them. The guard
pins their count.

**`baseline/exemptions.txt` is empty**, and that is a result rather than a
default: no SPARQL 1.0 negative case turned out to be relaxed by 1.1, and no
evaluation case needs one. `eng/ratchet.cs` fails the build if any of those
2,525 stops passing, and an exemption with no written justification fails the
run too.

Also true today, and measured rather than asserted:

- **Zero bytes allocated per quad**, on every entry point in every syntax —
  measured as the difference between a 500-quad and a 4,000-quad parse, because
  an absolute figure measures the harness as much as the parser.
- **Native AOT and browser WebAssembly** both read and write Turtle. CI
  publishes the AOT binary and *runs* it; the interesting AOT failures are at
  run time and silent.
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

**RDF 1.2 Turtle and TriG are not accepted at all** — deliberately, rather than
half-accepted. `docs/spec/turtle.md` §9 lists the constructs and the reasoning.
SPARQL 1.2 is accepted in full, triple terms, reifiers, annotations and
`VERSION` included, because the algebra was built with 1.2 from the start.

**Queries evaluate; updates do not yet.** `Varve.Sparql.Evaluation` answers
`SELECT`, `ASK`, `CONSTRUCT` and `DESCRIBE` over any quad source; executing an
update, writing results and HTTP federation are milestone 5c. `NOW()` and the
random functions read a clock and a random source the caller supplies —
`Clock = TimeProvider.System` for the system clock — and fail by the option's
name without one (ADR 0056).

**Nothing is published yet.** The package metadata and the trusted-publishing
workflow are in place and await the first `v0.1.0-preview.1` tag.

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

Not built. Milestone 7, with OIDC bearer authentication decided ahead of it in
[ADR 0037](docs/adr/0037-server-authentication.md), followed by an operability
milestone and a container image.

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

- [`docs/brief.md`](docs/brief.md) — the project brief. The authority.
- [`docs/adr/`](docs/adr/) — architecture decisions. Numbered, superseded
  rather than edited.
- [`docs/spec/`](docs/spec/) — functional specifications, per component.
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
