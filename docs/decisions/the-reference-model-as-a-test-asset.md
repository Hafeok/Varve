---
set: the-reference-model-as-a-test-asset
namespace: varve
adr: 0043
decisions:
  - key: ReferenceModelFromTheSpec
    statement: "Varve.Store.Tests holds a reference model, a fold over the same requests written from the specification alone, over terms rather than ids"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: ModelSharesNoCode
    statement: "The reference model shares no code with the store, not even a helper"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: ModelIsNaiveOnPurpose
    statement: "The reference model is deliberately slow and correct by inspection"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: GeneratorsCountTheirCases
    statement: "The model's generators count every case they must produce, and a run fails if any case never occurred"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: ValidatorsStatedTwice
    statement: "Validators are stated over the store's types and over the model's term sets, and their verdicts must agree before anything else is compared"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: ModelPropertyAfterEveryRequest
    statement: "After every generated request the property compares the outcome and position, G_P and the dictionary's growth, and over the run as-of reads, diffs and the settings fold"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: ShrunkCounterexamplesKept
    statement: "Each shrunk counterexample becomes a named regression case beside the property"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: SpecificationDecidesDisagreements
    statement: "When store and model disagree the specification decides, and a disagreement it does not settle is reported as a finding about the specification"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
---

The rulings of [ADR 0043](../adr/0043-the-reference-model-as-a-test-asset.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
