# 0003 — Package layering and the strictly downward reference rule

## Status

Accepted. 2026-09-20. **Its layer table is superseded by
[0060](0060-hosts-at-layer-6-the-composition-root.md)**: integrations stay at
layer 5, hosts move to a layer 6 reserved to executables, and benchmark
assemblies declare a layer rather than `none`. The rest stands.

Enforced by `VARVE0001` and `VARVE0002`. See
`docs/rules/VARVE0001.md`, `docs/rules/VARVE0002.md` and ADR 0004.

## Context

`docs/brief.md` states three principles governing package and type boundaries —
stable dependencies, low coupling, high cohesion — and requires that they are
checked rather than aspired to. The first of them is the one with teeth: the
package graph is a DAG with fixed layers, and a package may reference only
packages in a lower layer.

The reason to fix this now, before any package exists, is that layering is
cheap to declare and expensive to retrofit. A cycle introduced in month three
is discovered in month nine as an inability to publish one package without the
other, and by then the fix is a redesign.

## Decision

Six layers. A package declares its layer; a reference is legal only if the
referenced package's layer is **strictly lower**.

| Layer | Packages | Owns |
|---:|---|---|
| 0 | `Varve.Iri`, `Varve.Xsd` | No Varve dependencies. RFC 3987 IRIs; XSD datatypes with SPARQL operator semantics. |
| 1 | `Varve.Rdf` | The RDF model and the abstract quad source contract. |
| 2 | `Varve.Turtle`, `Varve.RdfXml`, `Varve.JsonLd`, `Varve.Sparql.Results`, and the SPARQL algebra and parser | Syntax. Reading and writing, and the algebra's own shape. |
| 3 | SPARQL optimiser and evaluator, `Varve.Shacl` | Evaluation over the quad source contract. Knows nothing about the store. |
| 4 | `Varve.Store` | The log, the projection contract, the pre-commit validator contract. |
| 5 | Integration and hosts: SHACL store integration, SPARQL Update integration, `Varve.Server`, the CLI | Composition. The only layer that may know about both a store and an evaluator. |

**Same-layer references are violations**, not exceptions. Two packages in one
layer that need each other are one package or two layers; the rule forces that
question to be answered rather than deferred.

Contracts live in the lowest layer that can define them without knowing their
implementers. `Varve.Rdf` at layer 1 defines the quad source contract; the
evaluator at layer 3 and the store at layer 4 both depend on it, and neither
depends on the other.

A lower layer never learns about a higher one — including indirectly, through a
callback typed to a concrete higher-layer type, through service location, or
through `InternalsVisibleTo`. `InternalsVisibleTo` is permitted toward `*.Tests`
assemblies only, which will be `VARVE0003`.

**Declaration.** Each project sets the MSBuild property `VarveLayer`, surfaced
to the compiler with `CompilerVisibleProperty`, and `Directory.Build.targets`
emits it into the assembly as `[assembly: AssemblyMetadata("Varve.Layer", n)]`.
The BCL attribute is used deliberately: a Varve-defined attribute type would
have to live in a package below layer 0, which is the same as saying it would
break the rule it exists to express.

Test and benchmark assemblies, and `Varve.Analyzers` itself, declare
`VarveLayer=none`. They compose across layers by nature — a test for the
evaluator may need a store — and are not published as libraries, so the
stability argument does not apply to them.

## Alternatives considered

- **Convention and review.** Rejected by the brief: a rule that exists only in a
  document is not a rule. Layer violations are also exactly the kind of mistake
  that looks locally reasonable in a pull request and is only visible from the
  whole graph.
- **Enforcing at publish time**, by checking the produced `.nupkg` dependency
  graph in CI. Catches real violations, but late: the author learns at CI rather
  than at the keystroke, and the fix by then is a refactor. Worth adding later
  as a second net — it sees the package graph, which an analyzer cannot — but
  not as the primary gate.
- **NuGet `PrivateAssets` and manual reference hygiene.** Controls transitive
  flow, not direction. Says nothing about whether a reference points up.
- **Deriving the layer from the package name** rather than declaring it. Rejected:
  it makes the rule unable to see a new package until someone edits the analyzer,
  and it would make the layer an emergent property of naming rather than a stated
  decision.
- **Computing instability, `I = Ce / (Ca + Ce)`, and gating on it.** Cannot be an
  analyzer — an analyzer sees one compilation at a time. If we want it, it is a
  CI report, never a gate. Noted in `docs/roadmap.md`.

## Consequences

Every new package is a decision about which layer it belongs to, made before its
first line of code. That is the point, and it is also the cost: a package whose
layer is genuinely unclear is a signal that its responsibility is unclear.

The strict rule forbids some references that are locally harmless. The escape is
an ADR and a suppression citing it (see ADR 0004), not a quiet exception.

`VARVE0002` exists because `VARVE0001` can otherwise be bypassed by omission: an
assembly that declares no layer would have nothing to compare against. A
referenced `Varve.*` assembly with no layer metadata is therefore an error in
its own right.

### Open question 1 — `Varve.Shacl` and the SPARQL evaluator are both in layer 3

The brief places `Varve.Shacl` and the SPARQL optimiser and evaluator in layer 3,
and separately says SHACL is "built after the SPARQL evaluator, since
SHACL-SPARQL depends on it". Under the strict rule, and because same-layer
references are violations, that dependency is illegal as stated. The two
statements cannot both hold.

Recorded, not resolved. The candidate resolution named in the project prompt:
`Varve.Shacl` stays at layer 3 depending on `Varve.Rdf` only, and defines a small
contract for executing a SPARQL-based constraint — given a shapes graph, a focus
node and a query, yield bindings. `Varve.Shacl.Sparql` sits one layer up and
implements that contract against the evaluator. This keeps the brief's stated
requirement that the validator is usable standalone over any quad source, and it
matches the pattern ADR 0005 already applies to the store. It has a cost worth
naming: SHACL Core and SHACL-SPARQL become two packages, and a user who wants
the whole of SHACL 1.2 installs both.

To be decided no later than milestone 8, and earlier if the layer 3 boundary is
touched before then.

### Open question 2 — optimiser and evaluator are both in layer 3

The brief lists the SPARQL optimiser (`sparopt`) and evaluator (`spareval`) as
separate packages in the same layer. If the evaluator consumes a plan type that
the optimiser owns, the reference is same-layer and therefore illegal; the
options are that they are one package, or two layers, or that the plan type
belongs to neither and lives in layer 2 with the algebra.

Recorded, not resolved — the answer depends on whether the plan is an annotated
algebra tree, which argues for layer 2, or a distinct physical plan, which argues
for splitting the layers. Nothing before milestone 5 depends on the answer.

Whichever way both questions go, the resolution is a superseding ADR, not an edit
to this one.

### Amendment, 2026-09-24 — open question 2 is closed

Closed by [ADR 0048](0048-optimiser-and-evaluator-one-package-algebra-in-algebra-out.md):
the optimiser's output is algebra, there is no plan type, and the optimiser
and evaluator share one layer 3 package, `Varve.Sparql.Evaluation`, above the
layer 2 algebra and parser package `Varve.Sparql`. The layer table above is
unchanged in substance; its "SPARQL optimiser and evaluator" and "SPARQL
algebra and parser" entries now have package names. Open question 1 remains
open, due at milestone 8.
