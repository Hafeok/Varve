---
set: per-run-term-filters
namespace: varve
adr: 0108
decisions:
  - key: PerRunTermFilter
    statement: "Each run carries a blocked Bloom filter over its terms' content hashes, eight bits a term, consulted before the run's hash index on a lookup by term"
  - key: DerivedFormat3
    statement: "The filter is a section of the run file named in its directory, and the derived format version is 3; a format-2 run has no filter and is read as before, migrating when maintenance rewrites it"
  - key: FilterLoadedWithDirectory
    statement: "A filter is loaded with the run's directory, held while the reader holds the run, and counted in the soak's dataset's-own figure"
---

The rulings of [ADR 0108](../adr/0108-per-run-term-filters.md), filed unaccepted by milestone 7b of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
