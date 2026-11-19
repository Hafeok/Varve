---
set: owl-2-rl-and-rdfs-as-rules
namespace: varve
adr: 0197
decisions:
  - key: InferenceModelNamespace
    statement: "The values the engine takes and answers, a fact, a profile and a clash, are the model, in Varve.Inference.Model; the rules, the joins and the closure are not"
  - key: TwoMaterialisingProfiles
    statement: "varve:RDFS materialises the RDFS entailment rules and varve:OWL2RL OWL 2 RL/RDF over them, while rdf, rdfs and d stay answered by the rewriting"
  - key: NoRuleInventsATerm
    statement: "Axiomatic triples are bounded to the container membership properties that occur, and a rule that would introduce a blank node for a literal is held in its generalised form over the literal, never served"
  - key: RulesCompiledByTheTBox
    statement: "The rules are specialised by the binding's TBox into join tables, recompiled by a generation"
  - key: SameAsOff
    statement: "With varve:SameAsOff owl:sameAs is an ordinary triple: the rules that conclude it fire, the eq-* substitution rules do not, and the cases that need them are exempted by name until part 3"
  - key: ClashReportedNotServed
    statement: "A rule with a false head is a check whose firing is a clash, reported on readiness as inconsistent with its count, named in the binding's description and in explain, and a query under owl2-rl over such a graph is answered from the fixpoint with Varve-Inconsistent"
  - key: OwlRlServedByTheClosure
    statement: "owl2-rl is answered by simple evaluation over the explicit quads and the rows valid at the position, with C1 applied, on graphs an OWL2RL binding covers, and is entailment-unsupported elsewhere"
  - key: NoClaimWiderThanServed
    statement: "Ruled 2026-11-19: a name or a claim Varve exposes is never wider than what is served, and an answer narrower than its request suggests says so in-band; a graph under an OWL2RL binding with varve:SameAsOff is described with the profile varve:OWL2RLWithoutEquality and its varve:sameAs, never pr:RL, which is claimed only where the equality rules are in effect"
  - key: EqualityNotAppliedSaidInBand
    statement: "Ruled 2026-11-19: every answer under owl2-rl by a binding with varve:SameAsOff carries Varve-Equality: not-applied"
  - key: PerGraphClosureSaidInBand
    statement: "Ruled 2026-11-19: an answer under owl2-rl whose default graph merges two or more graphs is the union of each graph's closure, and carries Varve-Closure: per-graph"
---

The rulings of [ADR 0197](../adr/0197-owl-2-rl-and-rdfs-as-rules.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 2 (#171) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
