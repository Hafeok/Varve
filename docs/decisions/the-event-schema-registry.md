---
set: the-event-schema-registry
namespace: varve
adr: 0145
decisions:
  - key: SchemaComputedOnRead
    statement: "An event type's JSON Schema, draft 2020-12 with additionalProperties true, is generated from its declaration when read and never stored"
  - key: RevisionsAreImmutable
    statement: "A schema's $id is …/versions/{n}/revisions/{r}, r a position at which the generated schema changed, immutable; …/versions/{n} and the bare URL resolve to the latest"
  - key: GeneratorGoldenFiles
    statement: "The generator's output is under the read-forever rule: golden files per case and revision, and a generator change that alters a byte fails the tests"
  - key: CompatibilityCheckedAtCommit
    statement: "A dataset-level validator refuses a declaration change that is malformed (event-declaration-invalid) or incompatible at an unchanged version (event-schema-incompatible), with nothing committed"
  - key: CompatibleMeansOldAcceptsNew
    statement: "A change keeps the version only when every instance valid under the new schema is valid under the old one; versions may skip and never decrease"
  - key: RetiredNeverDeleted
    statement: "A declaration is never removed; a type is retired with varve:eventRetired true"
  - key: ValidatorProblemTypes
    statement: "A validator rejection whose report starts with a catalogued validator problem type's IRI and an rdf:JSON literal of its members is answered as that type"
---

The rulings of [ADR 0145](../adr/0145-the-event-schema-registry.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration, part 2,
of #93 (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
