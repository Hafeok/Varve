# 0141 — RealEstateCore as the reference domain for learning and testing

## Status

**Accepted — filed unaccepted by milestone Object storage of #92, 2026-10-15**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on 2026-10-14: RealEstateCore (REC) is the worked domain of
the learning track and the realistic test case across milestones. Filed from
the maintainer's draft, which is binding on this milestone; its text is kept,
with the house's sections and checks.

Milestones: Object storage (#92, the generator), Learning track (#94), SHACL
(#13), Inference (#95), and after 1.0 Continuous queries and Entity export.
Related: the ADX boundary (ADR 0140; telemetry belongs in an Eventhouse); the
Entity export and Inference grant decisions, when filed; constraint 4 (every
third-party dependency needs an ADR).

## Context

Today the samples and benchmarks have no shared domain. The learning track uses an order domain, and the benchmarks use generic or W3C test data. A single realistic domain would let every milestone be tested against the same shapes and the same data, and let the docs tell one story.

REC fits well:
- it is a published ontology for buildings, maintained by a consortium of property owners and used in production;
- it ships as SHACL in REC 4 (the OWL version is deprecated), which exercises the SHACL milestone with real-world shapes;
- its hierarchies are deep: the point and equipment classes are sourced from Brick, which exercises Inference tier 1;
- its structure covers what the learning track needs:
  - a part-of topology (`hasPart`/`isPartOf`, Region → Site → Building → Level → Room), for links and property paths;
  - cross-topology links (`locatedIn`, `hasPoint`);
  - collections (administrative units such as apartments or leases), for multi-tenancy;
  - agents, for grants and on-behalf-of;
  - events (`ObservationEvent`, `ActuationEvent`), for the line between state and telemetry;
- buildings change slowly and are linked, which is exactly Varve's ground in ADR 0140.

The REC repository is under the BSD-3-Clause licence.

## Decision

**1. REC 4 is vendored as test and sample data, not as a dependency.**
- The SHACL release files are copied into `testdata/rec/` at a pinned release tag, with a checksum and the BSD-3-Clause notice in `THIRD-PARTY-NOTICES`.
- No package references REC, and no shipped assembly contains it.
- Updating the pinned tag is a PR that reruns every REC-based gate.
- This ADR serves as the constraint-4 record for the vendored files.

**2. A deterministic portfolio generator.**
- It is a seeded generator in a test-only project, never shipped. It produces REC-conformant portfolios of regions, sites, buildings, levels, rooms, assets, points, collections and agents.
- It produces a history, not only a final state, with:
  - renovations: rooms split, merged or retyped;
  - assets moved between spaces;
  - points added and retired;
  - tenants entering and leaving collections.

  Those are retractions and link changes over time, which is what as-of, inference and entity export need to be tested on.
- Three named sizes, S, M and L, become the stated datasets for benchmarks. The Object storage session sets the quad counts. L must be large enough that cold start without segments is visibly worse.
- The same seed and size give the same bytes on every platform.

**3. Telemetry stays outside, and the samples show that.**
- `ObservationEvent` at sensor rate is telemetry. By the ADX boundary ADR it goes to an Eventhouse, joined to Varve on the point IRI.
- The samples show this split explicitly.
- Varve holds the points, their configuration and their history, and low-rate events: actuation commands and alarms raised as domain events.

**4. Where REC is used.**

| Milestone | Use |
|---|---|
| Object storage (#92) | builds the generator; S, M and L are the cold-start and segment datasets |
| Learning track (#94) | the running domain of every chapter (see the prompt addendum) |
| SHACL (#13) | REC 4 shapes validate generated portfolios standalone, at commit time and incrementally; the session reports which SHACL features REC uses and any shape it cannot evaluate |
| Inference (#95) | tier 1 rewriting over the REC and Brick class hierarchies; incremental closure equals from-scratch over generated histories |
| Continuous queries (after 1.0) | the benchmark's standing queries: rooms per building, points per space, assets by type |
| Entity export (after 1.0) | the sample fixture library exports a building or a room with its history |
| `docs/why.md`, operator guide | the Eventhouse pairing shown with REC points |

**5. REC is a test case, not a supported product feature.**
- Varve stays ontology-agnostic. No REC-specific code goes into any shipped package.
- If a REC test fails because of a Varve defect, Varve is fixed.
- If it fails because of a REC defect, the report goes upstream like #36–#39, with a written exemption until it is fixed.

## Alternatives considered

- **Keep the order domain.** Rejected. It is invented, it has no published shapes, and it has no deep class hierarchy. It tests nothing the W3C suites don't.
- **Brick Schema.** A strong ontology, and REC already sources its point and equipment classes from it. REC was preferred for two reasons: it also covers spaces, collections and agents, which the learning track needs; and it is SHACL-first. Brick-specific queries remain possible on the shared classes.
- **Several domains, one per chapter.** Rejected. The point is one story and one dataset across milestones.
- **Take REC as a NuGet dependency.** Rejected. The published packages are DTDL, not the SHACL release, and test data doesn't need a package.

## Consequences

- #92 gains the generator and states S, M and L: S about 200,000 quads at the head over 1,000 commits, M about 5,000,000 over 25,000, L about 50,000,000 over 100,000, generated from seed 20261015; about a quarter of the changes after a portfolio is built are retractions (`tests/Varve.Rec.Tests`).
- The learning-track prompt (#94) gets an addendum. The DDD chapter's order domain becomes REC bounded contexts.
- Issues #13 and #95 gain the REC gate items. The Continuous queries and Entity export issues name REC for their samples and benchmarks.
- `THIRD-PARTY-NOTICES` gains REC.

**Must change with this ADR:**
- Roadmap: the Object storage, Learning track, SHACL, Inference, Continuous queries and Entity export texts.
- The #92 addendum.
- The learning-track addendum.
- Handover document.

## Checks

- **Checked against the accepted ADRs** (0001–0133). Touches **0009** (no
  package is added: the vendored files are data, and this ADR is their
  constraint-4 record), **0027** (test data version-controlled, never fetched
  at test time). No conflict.
- **Layer ownership.** None shipped: `testdata/rec/` and the test-only
  generator, `tests/Varve.Rec.Tests`.
- **Analyzer rule.** None.
- **Open questions owned.** None.
