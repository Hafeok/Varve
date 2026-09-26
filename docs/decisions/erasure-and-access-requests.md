---
set: erasure-and-access-requests
namespace: varve
adr: 0023
decisions:
  - key: ErasureIsCryptoShredding
    statement: "Erasure is crypto-shredding: the data stays and the key is destroyed"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ErasureModePerDatasetOffByDefault
    statement: "Erasure mode is per dataset, off by default and set by a Settings commit, and costs nothing but a reserved id class when off"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: KeyStoreContract
    statement: "The key store creates a key per data subject, resolves a subject to a KeyId, fetches key material by KeyId and destroys a key, and lives outside the dataset directory by construction"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: KeyStoreOnlyMutableComponent
    statement: "The key store is the only mutable, deletable component, and its backups must honour destruction"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ClassifierContract
    statement: "A classifier assigns the term occurrences of pending operations to data subjects over the pinned source, free of SPARQL and SHACL, with derived implementations at layer 5"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: AccessByKeyId
    statement: "Access(K) is every dictionary entry under K, every quad of any commit mentioning one with the positions and timestamps it was asserted and retracted, and the metadata of commits whose agent is under K"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: AccessOmitsOthersAgents
    statement: "Agents of other commits appear in Access(K) only when canonical or under K, per GDPR Article 15(4)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: AccessScopeSetting
    statement: "Access scope is AllHistory or Current, stated per request or taken from a dataset setting that defaults to AllHistory"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: SelectorWithoutErasureMode
    statement: "Without erasure mode, access is served by an optional selector over G_head, and erasure cannot be served at all"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: EraseRetractsByKeyId
    statement: "Erase(subject) retracts from G_head every quad mentioning a term under the subject's key unless the caller opts out, appends an Erasure commit carrying the KeyId, agent and cause, then destroys the key"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: PlaintextConfinement
    statement: "No file in log/, no checkpoint and no filtered subscription record ever holds a private term's plaintext or key material, which exist only in memory and under derived/ tagged by KeyId (I9)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ErasureSoundness
    statement: "After Erase, every private entry under the destroyed key is unreadable in the log, in every checkpoint and in every copy of the dataset directory made at any time (I10)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: StructureSurvivesErasure
    statement: "Erasure leaves structure: shredded ids and their links survive, and whether the residue is anonymous is a legal question"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: AsOfReadsStructurallyStable
    statement: "As-of reads are structurally stable for ever, and erasure changes only the readability of private terms, at every position at once"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: CommitAgentIsATermId
    statement: "A commit's agent is a TermId, never an inline string, so the agent can itself be a private term and be erased"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0023](../adr/0023-erasure-and-access-requests.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Carries the rulings of ADR 0019, which 0023 superseded whole. The classification gate is ADR
0017's `ClassificationGateIsPolicy` and is not repeated here.
