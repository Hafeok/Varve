---
set: private-ids-reserved-and-the-key-store-refused
namespace: varve
adr: 0074
decisions:
  - key: PrivateEntryLayout
    statement: "A private dictionary entry is term kind 4: a 16-byte key id, a 32-byte synthetic IV and the ciphertext with its length; an Erasure commit's payload is a 16-byte key id, and settings field 2 is erasure mode"
  - key: PrivateEntriesRefused
    statement: "The decoder reads private entries, Erasure payloads and the erasure-mode setting, and the store refuses to open a log holding them until erasure mode exists"
  - key: DirectoriesAreWrappers
    statement: "DatasetDirectory and KeyStoreDirectory are wrappers over a normalised absolute path, compared segment by segment from the full path, case-insensitively on Windows only, and no string path is on FileStorage or Dataset"
---

The rulings of [ADR 0074](../adr/0074-private-ids-reserved-and-the-key-store-refused.md), filed
unaccepted by session 6a of #10 (ADR 0066). The refusal itself is ADR 0018's
`NoKeysInDatasetDirectory`, already accepted, which the file backend cites.
