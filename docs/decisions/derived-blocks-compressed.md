---
set: derived-blocks-compressed
namespace: varve
adr: 0080
decisions:
  - key: KeyBlocksAreDeltaCoded
    statement: "Keys in derived runs are stored in their blocks of 128, the first key whole and each later one as the first id that differs, its increase and the ids after it as varints, with each block's start in the directory; derived format version 2"
---

The rulings of [ADR 0080](../adr/0080-derived-blocks-compressed.md), filed unaccepted by session
6c of #10 (ADR 0066).
