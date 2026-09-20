# Varve

A graph database and RDF/SPARQL toolkit for .NET, event-sourced from the first
commit. The transaction log is the source of truth; every index is a projection
of it. A read is pinned to a log position, so snapshot isolation, time travel and
change feeds are properties of the model rather than features bolted on later.

The name is geological: a varve is one annual sediment layer, countable and
datable. One commit is one varve. The vocabulary stays in the documentation —
public API names are conventional (`Commit`, `Position`, `Projection`).

Scope matches Oxigraph: RDF model, IRI and XSD datatypes, the syntax family
(N-Triples, N-Quads, Turtle, TriG, RDF/XML, JSON-LD), SPARQL algebra, optimiser
and evaluator, the store, a server and CLI, and a SHACL validator. Oxigraph is
the reference for scope and for conformance behaviour, not for architecture; no
code is ported from it or from dotNetRDF.

## Status

Milestone 1. There is no production code yet, by design — the gates come first:

- `Varve.Analyzers` enforces the package layering rule at build time.
- The W3C RDF 1.1 N-Triples and N-Quads test suites run in CI from this commit
  onward. Every case currently fails with "no parser registered". That is the
  correct result, and the baseline ratchet in `eng/ratchet.cs` is what will stop
  it silently regressing once cases start to pass.

`docs/roadmap.md` has the milestone list.

## Constraints

1. 100% managed code. No P/Invoke, no native assets.
2. Native AOT and trimming compatible.
3. One core, three hosts: embedded library, server, browser (WASM).
4. Minimal dependencies. Every third-party package has an ADR.
5. Current LTS .NET and current C#. Allocation per quad is a defect.
6. Permissive licence, no copied code.

`docs/brief.md` is the authority for all of the above and is not summarised
accurately anywhere else, including here.

## Building

```
dotnet build Varve.slnx -c Release
dotnet test  tests/Varve.Analyzers.Tests
dotnet test  tests/Varve.Conformance.Tests
```

The conformance suite needs the W3C submodule:

```
git submodule update --init --recursive
```

## Documentation

- `docs/brief.md` — the project brief. The authority.
- `docs/adr/` — architecture decisions. Numbered, superseded rather than edited.
- `docs/rules/` — one page per `VARVE` analyzer rule, each linking to its ADR.
- `docs/spec/` — functional specifications, per component.
- `docs/roadmap.md` — milestones and what is deferred.

## Licence

Apache-2.0. See `LICENSE` and `docs/adr/0002-licence.md`.
