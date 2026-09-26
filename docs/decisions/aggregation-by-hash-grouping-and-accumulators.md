---
set: aggregation-by-hash-grouping-and-accumulators
namespace: varve
adr: 0053
decisions:
  - key: AggregationFollowsTheAlgebraLiterally
    statement: "Aggregation follows SPARQL section 18.5.1 literally: aggregates are extracted once per Group into slots no author can name, and a non-key variable in one reads as SAMPLE"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: GroupKeysByTermEquality
    statement: "A group key is its key expressions' values under the source's term equality, an error being a key value of its own that leaves the key variable unbound"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ImplicitGroupOverEmptyInput
    statement: "A Group with no keys over an empty input yields one empty group, and a Group with keys yields none"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: AccumulatorPerAggregate
    statement: "Each aggregate is an accumulator fed one solution at a time per group, handling errors and empty groups as the ADR's table states"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: AggregateErrorLeavesUnbound
    statement: "An aggregate whose result is an error leaves its binding unbound and never fails the query"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ErrorsInMinMaxAndSample
    statement: "An error makes MIN and MAX an error, and SAMPLE returns a value that is not one, following Oxigraph where section 18.5.1 is silent"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: DistinctAggregateIsASet
    statement: "DISTINCT in an aggregate is a set per accumulator keyed by term equality, and COUNT(DISTINCT *) keys on the whole solution"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: GroupConcatInInputOrder
    statement: "GROUP_CONCAT has no ORDER BY, concatenates in input order and always yields a simple literal"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: CustomAggregatesFromOptions
    statement: "A custom aggregate is an IExtensionAggregate in the evaluator's options, and an unregistered IRI fails at compile time naming it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: HashAggregationWithoutSpill
    statement: "Aggregation is one in-memory hash table per Group with no spilling, bounded only by the caller's resource governance"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: GroupConcatComparedAsMultiset
    statement: "The optimiser's equivalence property compares GROUP_CONCAT results as multisets of their parts"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0053](../adr/0053-aggregation-by-hash-grouping-and-accumulators.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
