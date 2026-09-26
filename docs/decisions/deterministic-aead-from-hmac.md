---
set: deterministic-aead-from-hmac
namespace: varve
adr: 0028
decisions:
  - key: SivAeadFromHmac
    statement: "Private terms are encrypted by a deterministic, misuse-resistant SIV AEAD built from HMAC-SHA-256 alone"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: VersionedKeySeparation
    statement: "The MAC and encryption keys are derived from the subject key by HKDF-SHA-256 under distinct, versioned info strings"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: SlotBoundAssociatedData
    statement: "The associated data is version, key id, term id, position and allocation index, each fixed-width, and removing a slot field reintroduces the equality oracle"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: ReleasePlaintextOnlyAfterVerify
    statement: "Decryption recomputes the tag, compares it with FixedTimeEquals and releases the plaintext only on equality"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: FullWidthSyntheticIv
    statement: "The synthetic IV and tag is the full 32 bytes of HMAC-SHA-256"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: PaddingIsADatasetSetting
    statement: "Length-hiding padding to a multiple of 16 bytes, applied before the MAC, is a dataset setting off by default"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: ExternalReviewBeforeShipping
    statement: "Nothing presents the construction as protecting data until an external cryptographic review, due before milestone 9 ships, and a rejection means erasure mode does not run in the browser"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: KnownAnswerVectorsOnEveryHost
    statement: "The implementation carries known-answer vectors that give identical bytes on CoreCLR, Native AOT and browser WebAssembly"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: BrowserPrimitivesAsserted
    statement: "The browser smoke test asserts on every run which cryptographic primitives the browser has and lacks"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
---

The rulings of [ADR 0028](../adr/0028-deterministic-aead-from-hmac.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Carries the cipher decision of ADR 0020, which 0028 superseded whole.
