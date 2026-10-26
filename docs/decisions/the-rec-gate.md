---
set: the-rec-gate
namespace: varve
adr: 0176
decisions:
  - key: GateComposition
    statement: "The REC gate's shapes graph is REC 4.1.0's rec.ttl, Brick+patches.ttl and recimports.ttl as one RDF merge, each file's blank nodes its own, with testdata/rec/gate/composition.ttl: owl:Class rdfs:subClassOf rdfs:Class, and the sh:declare prefix declarations Brick's three SPARQL constraints need, owl: and brick: included, read under varve:subClassOfInShapesGraph"
  - key: VendoredStaysRefused
    statement: "REC 4.1.0 as vendored, without the composition, is refused for its undeclared SPARQL prefixes, and a test pins the refusal; the defect is reported upstream with no exemption taken"
  - key: ThreeWaysAgree
    statement: "Standalone validation, commit-time gating under reject and the validation projection agree on generated REC portfolios: REC S at every 250th position and the last on every pull request and at every position nightly, REC M at 100 positions nightly, with every commit of the history accepted"
  - key: FeaturesReportPinned
    statement: "Which SHACL features REC uses, the class each falls in, and the shapes that cannot be evaluated are reported in testdata/rec/gate/features.md, written and pinned by a test"
  - key: OperatorsAddTheComposition
    statement: "The operator's guide tells a user binding REC or Brick shapes to add both parts of the composition to their shapes graph"
---

The rulings of [ADR 0176](../adr/0176-the-rec-gate.md), one line each. The ADR is the narrative;
this is what code cites. Filed unaccepted by SHACL, part 2b (#13) (ADR 0066); every citation is
`CS0618` until the maintainer accepts them.
