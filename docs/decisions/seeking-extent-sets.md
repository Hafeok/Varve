---
set: seeking-extent-sets
namespace: varve
adr: 0177
decisions:
  - key: SeekDontNarrow
    statement: "A read at a position inside a format-5 extent set's window looks its keys up in the set as a read of the present does and classifies only the matching rows' intervals against the position; it writes nothing and reads only the matching keys' blocks and column entries"
  - key: IntervalsBesideTheKey
    statement: "A format-5 extent's positions are fixed-width columns aligned with its key sections, one distance per open and per closing key, and its rows within are a thirteenth key section with a column of two distances a row, so a key's intervals are reached from the key"
  - key: ReadsOfThePresentNeverTouchAColumn
    statement: "A format-5 extent's asserted and retracted sections are format 4's byte for byte, and a read of the present reads them and nothing else"
  - key: EveryWriterWritesFormat5
    statement: "A checkpoint, a merge and a drop write their set in format 5 from the same rows format 4 had, whatever their inputs' format; a merge stays a pure function of its inputs' rows"
  - key: Format5ForExtentsOnly
    statement: "Derived version 5 is an extent's alone; the dictionary extent, the graph-version index, the commit index, the catalog, runs and the projection's state are still written as version 4"
  - key: AnEstimateInsideASetMayBeEstimated
    statement: "An as-of view's estimate is exact when the sought set holds at most 4,096 rows in the pattern's range, and otherwise adds the set's count at its end and says it is estimated"
---

The rulings of [ADR 0177](../adr/0177-seek-dont-narrow-intervals-beside-the-key.md),
filed unaccepted by #133 (ADR 0066).
