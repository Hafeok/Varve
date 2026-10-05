---
set: the-term-dictionary-on-disk
namespace: varve
adr: 0079
decisions:
  - key: DictionaryIsDerivedInRuns
    statement: "The term dictionary is derived state carried by the default projection's runs: each run holds the entries of the canonical ids its commits allocated, a checkpoint those of every id up to its position, merged and persisted with the runs, so opening a dataset loads no dictionary"
  - key: LookupsReadThroughTheBlob
    statement: "A dictionary lookup by id reads an entry's offsets and bytes, and a lookup by term searches a hash index sorted by a 64-bit hash of the term's key by interpolation, both through the synchronous blob read of the runs the reader holds"
  - key: DictionaryCachesAreBounded
    statement: "The dictionary keeps two caches of fixed size, term to id and id to term, one slot per hash; a lookup that misses and finds the term allocates the slot's entry, and nothing else on a lookup allocates"
---

The rulings of [ADR 0079](../adr/0079-the-term-dictionary-on-disk.md), filed unaccepted by session
6c of #10 (ADR 0066).
