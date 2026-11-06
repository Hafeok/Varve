# Varve.Inference

The closure of an RDF graph under a materialising profile, over the handles of
any `IQuadSource`, at layer 4 (ADR 0191): the rules, their joins, and the
passes that keep the closure as facts are asserted and retracted.

- **`Closure`**: one graph's explicit facts, the profile's axioms, and every
  fact the rules conclude, to the least fixpoint. `Apply` takes an exact
  delta; the closure after equals the closure from scratch (ADR 0195 point 9).
- **Profiles** (ADR 0197): `InferenceProfile.Rdfs`, the RDFS entailment rules
  of RDF 1.1 Semantics §9.2, bounded so that no rule invents a term;
  `InferenceProfile.Owl2Rl`, OWL 2 RL/RDF over them, with `sameAs` off.
- **Retraction** (ADR 0196): semi-naive insertion through `Varve.Derivation`'s
  `Propagation`, and deletion as a second propagation. The candidates are
  measured on REC M before one is kept.
- **`IInferenceTerms`**: what a closure needs of its source, a handle for each
  term a rule names and what a handle stands for where a rule tests it.

The projection that keeps a closure per binding as of every position is
`Varve.Inference.Store`.
