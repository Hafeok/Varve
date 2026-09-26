# 0060 — Hosts at layer 6: the composition root, reserved to executables

## Status

**Accepted.** 2026-09-25. Decided by the maintainer at the close of milestone
5c. **Supersedes ADR 0003's layer table** — its layer 5 row and its sentence
that benchmark assemblies declare `none`. The rest of ADR 0003 stands: the
strictly downward rule, same-layer references as violations, the declaration
mechanism, and both open questions. Enforced by `VARVE0001` and `VARVE0002`;
see [`docs/rules/VARVE0002.md`](../rules/VARVE0002.md). **Enforcement moves** to `DD0001` and
`VARVE0005` ([0062](0062-adopting-decisiondriven-analyzers.md),
[0064](0064-varve-configuration-and-hot-path-rules.md), 2026-09-25); the
rulings are unchanged.

## Context

ADR 0003 put integrations and hosts in one row: "layer 5: SHACL store
integration, SPARQL Update integration, `Varve.Server`, the CLI". ADR 0005
then describes the shape the table forbids: the store defines contracts,
integrations compose them, and **hosts reference the integrations**. A host
at layer 5 that references an integration at layer 5 is a same-layer
reference, which `VARVE0001` reports and ADR 0003 calls a violation, not an
exception.

Milestone 5c was the first time it bit. `Varve.Sparql.Store` is the first
layer 5 integration, and the AOT and browser smoke apps, which were layer 5
hosts, could not reference it. The only routes were:
- declaring `none`, which `VARVE0002` reserves to tests, benchmarks and the
  analyzer by name;
- a suppression, which needs a recorded exception, and none existed.

5c composed the update by hand instead and raised the question
(`docs/traceability/2026-09-25-issue-9-milestone-5c-update-and-canonicalisation.md`,
proposal 4). `Varve.Server` meets the same wall at milestone 7, and the CLI
after it.

A benchmark assembly was the other half of the same problem. It declared
`none` because it composes across layers. But what it composes is the
public packages, as a host does, not internals, as a test does. Its
exemption was by name, and it was the same one that let a test compose
freely.

## Decision

**Seven layers.** Integrations stay at layer 5. Hosts get layer 6.

| Layer | Packages | Owns |
|---:|---|---|
| 0 | `Varve.Iri`, `Varve.Xsd` | Unchanged. |
| 1 | `Varve.Rdf` | Unchanged. |
| 2 | `Varve.Turtle`, `Varve.RdfXml`, `Varve.JsonLd`, `Varve.Sparql.Results`, `Varve.Sparql` | Unchanged. |
| 3 | `Varve.Sparql.Evaluation`, `Varve.Shacl` | Unchanged. |
| 4 | `Varve.Store` | Unchanged. |
| 5 | Integrations: `Varve.Sparql.Store`, the SHACL store integration | Composition of a store with an evaluator or validator, as a library. |
| 6 | Hosts: `Varve.Server`, the CLI, the smoke apps, the benchmarks | **The composition root** (below). May reference any layer below it. |

**The composition root — `ArchCompositionRoot` — is reserved to layer 6.**
The composition root is the one place where concrete choices are wired
together:
- the storage backend and its durability;
- the clock and the random source (ADR 0056);
- the load source and the `SERVICE` handler (ADRs 0055, 0057);
- the validators (ADR 0058).

A library never does this; it takes each of these as a parameter. The role is
identified with **being an executable**, and `VARVE0002` enforces the
equivalence in both directions:

1. **An executable declares layer 6.** A compilation whose output kind is an
   application (`OutputType` `Exe` or `WinExe`) and which declares an integer
   layer must declare 6. There is one exception: an executable that is
   exempt from layering entirely (a test assembly, whose xUnit v3 runner makes
   it an executable too) declares `none` as before.
2. **Layer 6 is declared only by executables.** A library at layer 6 would be
   a package nothing may reference, since nothing is above it. That is
   either a host that was not built as one, or an integration in the wrong
   row.

**Benchmark assemblies lose their `none` exemption** and declare 6. The
exemption by name now covers only `*.Tests` and `Varve.Analyzers`. A
benchmark assembly is an executable, so rule 1 already requires 6 of it.

Layer 6 assemblies may be packable. The CLI is expected to ship as a .NET
tool. Packing does not change the rules: nothing references layer 6.

## Alternatives considered

- **`none` for hosts**, by name or by an allow-list. Rejected. A host is in
  the published graph (the CLI will be packed), and `none` turns the
  direction check off for it. The point of the layers is that a host cannot,
  for instance, be referenced by an integration. With `none`, nothing stops
  that.
- **A recorded exception per host**: a suppression of `VARVE0001` citing an
  ADR, one per host. Rejected. It is the same exception every time, and ADR
  0003 says a same-layer need is "one package or two layers". This is two
  layers.
- **An opt-in property, `ArchCompositionRoot=true`, that only layer 6 may
  set**, without reference to the output kind. Rejected. It is a second
  declaration that can disagree with the first, and it leaves an executable
  at layer 3 unreported. Tying the role to the output kind makes it
  something the compiler already knows.

## Consequences

- **The analyzer.**
  - `LayerDeclaration.HighestLayer` becomes 6.
  - `VARVE0002` gains the two rules above, and drops `.Benchmarks` from the
    exemption by name.
  - `docs/rules/VARVE0001.md` and `VARVE0002.md` say so.
  - The analyzer tests cover each rule, including a test assembly that is an
    executable.
- **The smoke apps** move to layer 6 and run their update through
  `Varve.Sparql.Store`, retiring 5c's hand-composed update.
- **The benchmarks** move to layer 6, and gain a build-only CI job. They
  were outside the solution's build, which is how 5c broke them unseen.
- **`Varve.Server` and the CLI** are born at layer 6 (milestones 7 and later),
  with the pattern already exercised.
- The number of layers is now seven. ADR 0003's rationale does not depend on
  the count. The stability argument gets one more step, and it is the step
  with nothing above it.
