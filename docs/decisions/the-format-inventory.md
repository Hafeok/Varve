---
set: the-format-inventory
namespace: varve
adr: 0216
decisions:
  - key: TheInventoryIsTheTable
    statement: "docs/formats/README.md lists every format version a release wrote, with its version, the release that introduced it, its rule and its fixture, and eng/formats.cs checks that the table matches the version constants in the code"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
  - key: FixturesFromTheReleaseThatWroteThem
    statement: "Each format version's fixture under testdata/formats/ is written by a generator kept as text and run against the published packages of the release that introduced the version, with dataset ids and clocks fixed and SHA256SUMS beside it; a version no release has written yet is written by the branch that introduces it and kept as the next release's"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
  - key: ReadForeverFixturesReadAsWritten
    statement: "Every read-forever fixture is read by current code and compared with expectations its generator wrote from what it issued: the log at every position, the delta record by record, the CloudEvents and the schemas byte for byte, the coordination records field by field"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
  - key: DerivedFixturesReachTheSameResults
    statement: "Every derived-format fixture is opened by current code and gives its expectations at every position, by reading or by rebuilding"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
  - key: AVersionWithNoFixtureFails
    statement: "A format version in the code or the table with no fixture fails the gate, unless the table names it as a gap with its reason"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
  - key: AFormatChangeWithoutANewVersionFails
    statement: "Current code writing a fixture's workload writes the bytes of the current version's fixture, normalised only for what a fixture cannot fix and the inventory names; different bytes under the same version fail the gate"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
  - key: SavedPlansAreVersionedNotReadForever
    statement: "A varve apply saved plan is versioned and positional, not read forever: a format this version does not read is refused with a message that tells the operator to plan again"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-02T00:00:00Z
---

The rulings of [ADR 0216](../adr/0216-the-format-inventory.md), filed unaccepted
by 1.0 readiness A for #129 (ADR 0066).
