# RDF 1.2 Turtle and TriG, RDF/XML, JSON-LD

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#10](https://github.com/Hafeok/Varve/issues/10), milestone 6b |
| **Date** | 2026-10-09 |
| **Tool** | Claude Code 2.1.295, a cloud session started from the desktop app |
| **Model** | `claude-fable-5-1`, configured and served, from the session's own metadata, through the first pull request; `claude-opus-5-5` from 2026-10-10, switched by the maintainer, for the addendum below |
| **Session identifier** | `session_01F7hRfABzhAc7PaB87cNbU5` |
| **Branch** | `claude/affectionate-goodall-k5bmez`, from `main` at d02e0f1 |
| **ADRs** | 0121, 0122, 0123, filed unaccepted; 0027 and 0030 carry dated notes |

## The prompt

> Session 6b: RDF 1.2 Turtle and TriG, RDF/XML, JSON-LD
> 6b completes syntax conformance and unblocks the 41 SPARQL 1.2 evaluation cases the guard pins. Read `docs/roadmap.md` §6b, `docs/spec/turtle.md` (§9 above all), ADRs 0027 and 0061, the 3b and 5b traceability records, and the conformance harness. `AGENTS.md` applies in full; every new reader and writer is zero-allocation per quad, runs the chunk-boundary oracle, and is under the DD rules from its first line. Plan first, wait for approval; one PR, red only on `CS0618`; the descriptor `releases/v0.1.0-preview.4.yaml` is in the PR with `issue: 10` (rename to the next number if session O lands first); land through `land/`. Do not touch `Varve.Server`, `Varve.Protocol`, `Varve.Store` or `docs/operator/`; session O owns those in parallel. Merge `main` before close-out.
> A. Decisions
>
> 1. RDF 1.2 Turtle and TriG are accepted, superseding the decision `turtle.md` §9 records by a dated ADR: reifiers, annotations, triple terms and directional language tags in reader and writer, to the `rdf12/rdf-turtle` and `rdf12/rdf-trig` suites. The reason §9 gave (both drafts days old) has expired; the ADR cites the draft dates it was written against. Triple terms map onto the model's existing RDF 1.2 triple terms (milestone 3a); nothing in `Varve.Rdf` changes. The RDF 1.1 suites keep passing unchanged, since 1.2 syntax is a superset, and the ratchet guard that pinned the 41 blocked cases is deleted when they pass.
> 2. `Varve.RdfXml` (layer 2): reader and writer over `System.Xml.XmlReader` and `XmlWriter` (BCL, AOT-safe, streaming; the ADR records the alternative of a hand-written XML scanner and why not: namespaces, entities and encodings are the whole difficulty, and the BCL reader handles them), to the `rdf11/rdf-xml` suite, with `rdf:parseType="Triple"` and the RDF 1.2 additions where the suite has them. Zero allocation per quad is measured as before; where `XmlReader` allocates per element the figure is reported honestly and bounded, not hidden. When the suite passes, `tests/fixtures/w3c-rdfxml/` (ADR 0027's N-Triples translations) is deleted and the harness reads the originals; the dated note in 0027 is closed.
> 3. `Varve.JsonLd` (layer 2): JSON-LD 1.1 over `Utf8JsonReader`/`Utf8JsonWriter` (BCL, no `JsonDocument` on the hot path). Scope: `toRdf` (with expansion, since it requires it) and `fromRdf` as the reader and writer; `expand` exposed because it exists; `compact`, `flatten` and `frame` out of scope and recorded as such with the reason (they are document-shaping APIs, not RDF I/O; a later package if anyone needs them). Remote contexts load only through a caller-supplied document loader with the same shape as `LOAD`'s source (default: none, so a document referencing a remote context fails by name); the W3C suite runs with a loader over the local fixture tree. Suites: `json-ld-api` `toRdf` and `fromRdf` manifests, plus the `expand` manifest. The Turtle reader's chunk-boundary oracle applies to both readers.
> 4. Content negotiation for the server is session O's; this session only registers the two new media types in the format registry `Varve.Protocol` already reads, by adding the format descriptors in the layer-2 packages (`Varve.Protocol` discovers them through the existing contract, no change there).
>
> B. Definition of done
>
> * `rdf12/rdf-turtle`, `rdf12/rdf-trig`, `rdf11/rdf-xml`, the JSON-LD `toRdf`, `fromRdf` and `expand` manifests under the ratchet with guard counts; exemptions each with a written justification and Oxigraph's behaviour as the tie-breaker; the 41 blocked SPARQL 1.2 evaluation cases pass and their guard is removed.
> * Round-trip properties for each writer against its reader over generated datasets (as 3b did), including triple terms and directional literals; RDF/XML's property states which datasets it cannot express (predicates that are not valid XML names) and asserts the writer refuses them by name.
> * Allocation per quad measured for all three readers and writers, honest row first.
> * AOT and browser smokes parse each syntax.
> * `docs/spec/turtle.md` §9 revised; new `docs/spec/rdf-xml.md` and `docs/spec/json-ld.md` in the existing spec style, stating what is refused and why.
>
> Non-goals
> `compact`, `flatten`, `frame`; JSON-LD remote context fetching by the package; RDFa, HexTuples, anything not in the brief; any server or store change.
> Report
> As always: ADR numbers, suite counts with the ratchet total and every exemption, property iteration counts, allocation figures per syntax, the deleted fixture directory, proposed spec changes, and what milestone 8 needs from the maintainer.

The session read what the brief named, proposed a plan with five open
questions — whether a format registry exists in `Varve.Protocol` to register
media types in; whether RDF/XML's positions may be the XML reader's and
`System.Private.Xml` may join the browser build; the json-ld-api repository as
a second submodule with its 1.0-only cases excluded by rule; the default for
JSON-LD's `rdfDirection`; and when the Turtle writer declares `VERSION` — and
the maintainer approved it:

> Approved. 1: yes, and whichever session lands second adds the two media types to Varve.Protocol's table and 0092 at close-out after merging main. 2: yes, both costs recorded, with a revisit size in the ADR. 3: yes. 4: reversed: default is the native RDF 1.2 directional literal, the spec's modes and null are options, suite runs with each entry's stated option, the departure from the spec default stated in the spec page and the ADR. 5: VERSION "1.2" emitted automatically iff a 1.2-only construct is written, with an option to force; never silently absent. The empty rdf12/rdf-xml expected file becomes an ADR 0038 tracking issue for me to file.

## The report

**ADRs.** 0121 (RDF 1.2 Turtle and TriG are accepted; the edition decides a
surrogate escape; supersedes `turtle.md` §9; amends 0030), 0122 (RDF/XML
over `System.Xml`, its cost measured, a revisit size of 1.5 MB compressed;
closes 0027's fixture note), 0123 (JSON-LD 1.1 over `Utf8JsonReader` as a
tree in an arena; the direction is RDF 1.2's by default). Three decision sets,
filed unaccepted; `CS0618` is the one red.

**Suites and the ratchet.** `baseline/passing.txt` grows from 4,122 to
**5,507** lines: `rdf12/rdf-turtle` syntax 74 and eval 32, `rdf12/rdf-trig`
syntax 35 and eval 26, `rdf11/rdf-xml` 166 (the manifest's 7 commented-out
entries are not entries), `rdf12/rdf-xml` 31, `json-ld-api/toRdf` 448 of 467,
`expand` 368 of 386, `fromRdf` 53 of 54 (19, 18 and 1 excluded by the rule of
`json-ld.md` §1: `specVersion: json-ld-1.0`, `processingMode: json-ld-1.0`,
`produceGeneralizedRdf`), and the 41 SPARQL 1.2 evaluation cases that were
blocked, over four subjects. **Exemptions: 24**, the twelve `http-rdf-update`
ones unchanged and **twelve new**: `eval-triple-terms` `pattern-6`,
`pattern-7`, `pattern-10` and `graphs-2` over the store, its graph scope and
the protocol. Each matches a blank node inside a triple term; over
`InMemoryDataset` they pass through the new
`IQuadSource.TryGetTripleTermComponents`, and over the store they cannot until
the store implements it ([#87](https://github.com/Hafeok/Varve/issues/87),
see the addendum). pyoxigraph 0.5.11 gives
the suite's expected rows on all four. The `rdf12/rdf-xml` `tt-01` expected
file is empty on purpose — RDF 1.2 XML reads `rdf:parseType="Triple"` only
where `rdf:version` announces 1.2 — so no ADR 0038 tracking issue is owed for
it; the earlier note to the contrary is withdrawn.

**Properties.** Turtle: the 1.2 statements joined the existing generators.
RDF/XML: round trip 2,000 iterations, fixed point 1,000, refusals 500 and 200.
JSON-LD: round trip 2,000 (an exact isomorphism over the blank node
bijections), fixed point 1,000 up to the labels toRdf renumbers, the triple
term refusal 200. Every positive toRdf entry also runs the fromRdf writer's
round trip and fixed point in the conformance project. The chunk-boundary
oracle runs over the RDF/XML and JSON-LD suites through their
`ReadOnlySequence<byte>` entry points.

**Allocation**, measured as the difference between two documents, .NET 10
Release x64. Turtle: zero per quad on every path, fixed cost about 10.4 KB with
fourteen RDF 1.2 quads a statement. RDF/XML: **16 bytes per triple read and 34
written**, `XmlReader`'s and `XmlWriter`'s, the package's own code at zero;
bounds 24 and 48. JSON-LD: **0 bytes per quad for toRdf and for expand in a
steady state** (the per-thread processor keeps what a document grew; the first
document of a size costs the tree's growth), **286 bytes per quad for
fromRdf**, which buffers the dataset; bounds 64, 64 and 512.

**Browser.** `System.Private.Xml` was already in the bundle for the SPARQL XML
results format. The two packages add 436,835 bytes compressed (RdfXml 72,790,
JsonLd 88,439, plus symbols), 3.7 %, against a revisit size of 1.5 MB. The
AOT smoke publishes (9.9 MB) and runs all three syntaxes; the browser smoke
builds warning-free with them.

**Deleted.** `tests/fixtures/w3c-rdfxml/` with its hash guard, and the benchmark
project's `convert-rdfxml` command; dotNetRDF is back to the benchmark baseline.

**Spec pages.** `turtle.md` §1, §2, §4, §5, §7, §8 revised and §9 rewritten;
`rdf-xml.md` and `json-ld.md` new. Proposed changes to the W3C material, for
the maintainer's ADR 0038 judgement: none to file. Two observations are
recorded rather than reported — the RDF 1.2 XML suite gates triple terms and
directions on `rdf:version` but not annotations (`an-01`), which the reader
follows; and the json-ld-api `expand-0081` family expects a `@graph`
container to wrap a value that is already a graph object, which §5.1.2 step
13.12 does say.

**What milestone 8 needs from the maintainer.** Accept ADRs 0121–0123 on the
pull request. The store's `TryGetTripleTermComponents` is
[#87](https://github.com/Hafeok/Varve/issues/87), due before milestone 8: the
twelve exemptions leave when `Views.IndexSource`, `PendingSource` and the
staging view answer it, and the patch is a lookup of the term's components by
handle in the term table. The media types in `Varve.Protocol` and the ADR
0092 amendment are this session's, as the one landing second (addendum). JSON-LD compaction, flattening and framing are
recorded as out of scope with the trigger that reopens them.

**Commits.** `5d1e6fb` docs(adr) 0110; `ec9553d` feat(turtle); `acf385b`
docs(adr) 0111; `7f80500` feat(rdfxml); `4485932` docs(adr) 0112; `2101ea2`
feat(jsonld); and the close-out commit carrying the smokes, the CI jobs, the
status, the roadmap, this record and `releases/v0.1.0-preview.4.yaml`.

## Addendum, 2026-10-10: renumbered, and landing second

The Operability session (#12, pull request #86) filed ADRs from 0110 in
parallel and lands first. The maintainer, after #85 was opened:

> Hold for session O: both sessions filed ADRs from 0110, and O lands first. Renumber yours to 0121–0123 (and every reference, set file and the descriptor), take v0.1.0-preview.4, and merge main once #86 is in; at that close-out add the two media types to Varve.Protocol's table and the 0092 amendment, since you are the session landing second. The twelve triple-term exemptions stay as exemptions; open an issue for IQuadSource.TryGetTripleTermComponents due before milestone 8 rather than handing it to session O. Yes, watch #85.

Done in this order:

- **0110, 0111 and 0112 are 0121, 0122 and 0123**, in the files, the three
  decision sets, every reference in code, tests and documents, the ADR index,
  and the dated notes in 0027 and 0030. The commit subjects above keep the
  numbers they were written with. The release is **`v0.1.0-preview.4`**.
- **[#87](https://github.com/Hafeok/Varve/issues/87)** is the store's
  `TryGetTripleTermComponents`, due before milestone 8; the twelve
  exemptions, ADR 0121, README and the roadmap cite it.
- **A count corrected**: of the 41 cases unblocked, 37 pass over every
  subject and the other four over `InMemoryDataset` only. README and the
  roadmap had said 29.
- **CI on #85's first head** failed in every build-dependent job with
  `CS0618` and nothing else, as intended.
- The merge of `main` once #86 lands, and the two media types with the ADR
  0092 amendment, follow in the same pull request.

Developed with AI assistance under human review.
