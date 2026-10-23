# 0176 — The REC gate: its shapes composition, and three ways that agree

## Status

**Accepted — filed unaccepted by SHACL, part 2b (milestone 8, #13),
2026-10-23** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer:
- on the part 2 plan (#13, 2026-10-20): the gate adds
  `owl:Class rdfs:subClassOf rdfs:Class` to its shapes graph, which is OWL's
  own vocabulary and not a REC patch, and `docs/operator/shapes.md` tells
  users binding REC or Brick shapes to include it. It goes to REC upstream as
  a note, with no exemption;
- on #140 (2026-10-21): the gate also declares the prefixes Brick's SPARQL
  constraints leave undeclared, recorded here with the exact triples. The
  test pinning that REC 4.1.0 is refused as vendored stays. The upstream
  issue text is drafted on the pull request, for the maintainer to file.

## Context

ADR 0141 makes RealEstateCore 4 the reference domain, and milestone 8's REC
gate validates its generated portfolios with REC's own shapes. Part 2a (#140)
validated them standalone, and found two gaps in the vendored files:
- **No implicit class targets.** 1,445 Brick shapes are typed `owl:Class`
  and `sh:NodeShape`. They are implicit class targets (SHACL 1.2 Core
  §3.1.3.3) only when `owl:Class` is an `rdfs:Class`, which no file states.
- **Undeclared prefixes.** Brick's three SPARQL constraints name `rdf:`,
  `rdfs:`, `brick:` and `ref:` by `sh:prefixes`, but no file has a
  `sh:declare`. Two queries also use a prefix they do not list: `owl:` in
  `bsh:DeprecationRuleForInstances`, and `brick:` in
  `bsh:TimeseriesReferenceOnPointsConstraint`. Under SHACL 1.2 SPARQL §2
  (§5.2.1 of the 2017 Recommendation) the queries cannot be parsed, so the
  shapes graph is refused as it stands.

## Decision

1. **The composition.** The gate's shapes graph is the RDF merge of
   `rec.ttl`, `Brick+patches.ttl` and `recimports.ttl`, each file's blank
   nodes its own, and `testdata/rec/gate/composition.ttl`, read under
   `varve:subClassOfInShapesGraph`. The composition is exactly these
   triples:

   ```turtle
   owl:Class rdfs:subClassOf rdfs:Class .
   rdf: sh:declare [ sh:prefix "rdf" ; sh:namespace "http://www.w3.org/1999/02/22-rdf-syntax-ns#"^^xsd:anyURI ] .
   rdfs: sh:declare [ sh:prefix "rdfs" ; sh:namespace "http://www.w3.org/2000/01/rdf-schema#"^^xsd:anyURI ] .
   brick: sh:declare
       [ sh:prefix "brick" ; sh:namespace "https://brickschema.org/schema/Brick#"^^xsd:anyURI ] ,
       [ sh:prefix "owl" ; sh:namespace "http://www.w3.org/2002/07/owl#"^^xsd:anyURI ] .
   ref: sh:declare
       [ sh:prefix "ref" ; sh:namespace "https://brickschema.org/schema/Brick/ref#"^^xsd:anyURI ] ,
       [ sh:prefix "brick" ; sh:namespace "https://brickschema.org/schema/Brick#"^^xsd:anyURI ] .
   ```

   Each prefix node declares its own prefix. `brick:` also declares `owl:`,
   and `ref:` also declares `brick:`, so every prefix each query uses is
   declared on a node it lists. No constraint lists two nodes that declare the
   same prefix.
2. **The vendored files stay refused.** `RecStandaloneTests` pins that REC
   4.1.0 without the composition is refused for its three undeclared
   prefixes. No exemption is taken.
3. **Three ways agree.** Over the generated portfolio (generator output
   version 2), the shapes commit is followed by every commit of the history
   through a `reject` gate, each fed to the projection as it lands. Every
   commit is accepted. At each sampled position, while it is the head:
   - **standalone:** `BindingValidation.Validate` there;
   - **projection:** the projection's report there, byte for byte equal to
     the standalone;
   - **commit time:** the commit's attachment, equal to the digest of the
     projection's rows restricted to the commit's affected pairs.

   | Size | Positions | When |
   |---|---|---|
   | S, 1,001 commits | every 25th and the last, 41 | every pull request |
   | S | all 1,001 | nightly, `VARVE_LONG_PROPERTIES` |
   | M, 25,001 commits | 100, spread over the history, the last among them | nightly, `VARVE_LONG_PROPERTIES` |

   The nightly workflow, `.github/workflows/nightly.yml`, runs the SHACL test
   projects with `VARVE_LONG_PROPERTIES` set.
4. **The features report.** `testdata/rec/gate/features.md` lists:
   - the shapes by class;
   - the constructs that put them there;
   - every SHACL term the composed shapes graph uses, with its role, section
     and class;
   - the shapes that cannot be evaluated: none.

   `RecGateTests` writes it and pins it. Brick's SHACL-AF rules (`sh:rule`,
   5,947 triple rules and 78 SPARQL rules) are inference, which milestone 9
   owns (ADR 0149). A validation reads them as triples and nothing more.
5. **The operator's guide** (`docs/operator/shapes.md`) tells a user binding
   REC or Brick shapes to add both parts of the composition, and to bind with
   `varve:subClassOfInShapesGraph true`.

## Alternatives considered

- **Patching the vendored files.** Rejected on #13: they are REC's, pinned by
  checksum (ADR 0141), and the gate's additions are not REC's to carry.
- **Leaving Brick's SPARQL constraints out**, as part 2a's benchmarks did.
  The gate would then check less than REC states.
- **A lenient prefix fallback**, from each file's Turtle prefixes. SHACL does
  not license it, and the shapes graph would mean something different under
  Varve than under a strict processor.

## Consequences

- **REC S's agreement test runs on every pull request**, at about nine
  minutes on the session's machine. Of that, about 370 s is the history
  through the gate and the projection, and about 170 s is the 41
  comparisons. The cost per commit is the building commits': each adds a
  level of about 250 entities, and each entity runs Brick's two
  `brick:Entity`-targeted SPARQL constraints before and after. Part 2a's
  7.6 ms per commit was measured on event commits without them.
- **Upstream.** The undeclared prefixes are a REC defect, with
  `owl:Class rdfs:subClassOf rdfs:Class` as a note. The pull request carries
  the issue text for the maintainer to file.

## Checks

- **Checked against the accepted ADRs** (0001–0175). Touches **0141** (the
  vendored files and the generator), **0149** (SHACL-AF is inference's),
  **0151** (no `owl:imports` is followed, so the three files are merged by
  the caller), **0169** to **0174**. No conflict.
- **Layer ownership.** Test data and tests only. No shipped assembly carries
  REC (ADR 0141).
- **Analyzer rule.** None.
- **Open questions owned.** None.
