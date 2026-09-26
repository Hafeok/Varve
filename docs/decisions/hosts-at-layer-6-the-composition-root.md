---
set: hosts-at-layer-6-the-composition-root
namespace: varve
adr: 0060
decisions:
  - key: LayerTable
    statement: "Seven layers: 0 Varve.Iri and Varve.Xsd, 1 Varve.Rdf, 2 the syntaxes and Varve.Sparql, 3 Varve.Sparql.Evaluation and Varve.Shacl, 4 Varve.Store, 5 integrations, 6 hosts"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: CompositionRootIsTheExecutable
    statement: "The composition root, where storage, clock, randomness, load source, service handler and validators are wired, is reserved to layer 6 executables, and a library takes each as a parameter"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ExecutablesAreLayer6
    statement: "An executable declares layer 6 unless it is a test assembly, only an executable declares layer 6, and benchmark assemblies are layer 6 like any host"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: Layer6MayBePackable
    statement: "A layer 6 assembly may be packable, and nothing references layer 6"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0060](../adr/0060-hosts-at-layer-6-the-composition-root.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

`LayerTable` supersedes ADR 0003's and moved here from 0003's set. Enforcement moves to `DD0001`
and `VARVE0005` by ADRs 0062 and 0064; the rulings are these.
