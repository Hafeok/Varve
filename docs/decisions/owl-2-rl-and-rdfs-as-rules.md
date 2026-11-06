---
set: owl-2-rl-and-rdfs-as-rules
namespace: varve
adr: 0197
decisions:
  - key: TwoMaterialisingProfiles
    statement: "varve:RDFS materialises the RDFS entailment rules and varve:OWL2RL OWL 2 RL/RDF over them, while rdf, rdfs and d stay answered by the rewriting"
  - key: NoRuleInventsATerm
    statement: "Axiomatic triples are bounded to the container membership properties that occur, and a rule that would introduce a blank node for a literal is held in its generalised form over the literal, never served"
  - key: RulesCompiledByTheTBox
    statement: "The rules are specialised by the binding's TBox into join tables, recompiled by a generation"
  - key: SameAsOff
    statement: "With varve:SameAsOff owl:sameAs is an ordinary triple: the rules that conclude it fire, the eq-* substitution rules do not, and the cases that need them are exempted by name until part 3"
  - key: ClashReportedNotServed
    statement: "A rule with a false head is a check whose firing is a clash, counted on readiness and named in the binding's description, and a query under owl2-rl over such a graph is answered from the fixpoint with Varve-Inconsistent"
  - key: OwlRlServedByTheClosure
    statement: "owl2-rl is answered by simple evaluation over the explicit quads and the rows valid at the position, with C1 applied, on graphs an OWL2RL binding covers, and is entailment-unsupported elsewhere"
---

The rulings of [ADR 0197](../adr/0197-owl-2-rl-and-rdfs-as-rules.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 2 (#171) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
