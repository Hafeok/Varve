---
set: evaluator-options-extension-functions-clock-and-randomness
namespace: varve
adr: 0056
decisions:
  - key: EvaluationOptionsCarryTheOutside
    statement: "Everything the evaluator needs from outside, extension functions, custom aggregates, the clock and randomness, arrives in the immutable EvaluationOptions"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: UnknownExtensionFunctionIsAnError
    statement: "An extension function is an IExtensionFunction keyed by IRI, and a call to an unknown one evaluates to an expression error rather than failing the query"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: NowReadOncePerExecution
    statement: "NOW() reads EvaluationOptions.Clock once per execution, so every NOW() in a query agrees"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: RandomnessIsARandomSource
    statement: "Randomness is IRandomSource with one NextBytes member, from which RAND takes 53 bits and UUID and STRUUID take 16 bytes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: NoDefaultClockOrRandomness
    statement: "There is no default clock or randomness, and a query that needs one fails naming the option to set"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: EvaluatorUnderDeterministicBan
    statement: "Varve.Sparql.Evaluation is under the ambient clock and randomness ban, as Varve.Store is"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: OptimiserNeverMovesNondeterminism
    statement: "The optimiser never folds or moves a call to RAND, NOW, UUID, STRUUID, BNODE or an extension function"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0056](../adr/0056-evaluator-options-extension-functions-clock-and-randomness.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
