# 0157 — SHACL shapes for the event declaration vocabulary, shipped with `Varve.Events`

## Status

**Accepted — filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13), 2026-10-17** (ADR 0066). Decided by the maintainer on the part 1 plan.

## Context

ADR 0144 defines the event declaration vocabulary — `varve:EventType`,
`varve:EventField` and their properties — and `EventDeclarations` reads it,
refusing an ill-formed declaration by code. Milestone 8's close-out says
declarations "may be validated by shapes" (#13). Whether the server does so
instead of by code is part 2's decision; this part writes the shapes and
shows they agree with the code.

## Decision

1. **The shapes are a Turtle resource of `Varve.Events`**,
   `EventDeclarationShapes.ttl`, embedded in the assembly and readable as text
   through `EventDeclarationShapes.Turtle`. `Varve.Events` does not reference
   `Varve.Shacl`: the vocabulary's owner owns its shapes, and a caller who
   validates by shapes composes the two.
2. **What the shapes state**, by SHACL Core only, checked line by line
   against docs/spec/events.md §3.2 and `EventDeclarations`, with one
   catalogue graph as the data graph:
   - `varve:EventTypeShape`, targeting `varve:EventType`: the node is an IRI,
     not also a `varve:EventField`, and its only `rdf:type` is
     `varve:EventType`; exactly one `varve:eventName`, an `xsd:string`
     matching `^[A-Za-z][A-Za-z0-9._-]{0,127}$`; exactly one
     `varve:eventClass`, an IRI; exactly one `varve:eventVersion`, an
     `xsd:integer` from 1 to 2147483647, which is the code's range; exactly
     one `varve:eventSelect`, an `xsd:string`; at most one
     `varve:eventSubject`, an `xsd:string` matching the field-name pattern;
     at most one `varve:eventRetired`, an `xsd:boolean`; at most one
     `rdfs:comment`, an `xsd:string`; every `varve:eventField` value a
     `varve:EventField`; and `sh:uniqueValuesFor varve:eventClass` (SHACL 1.2
     §7.9.5), §3.2's "a class belongs to one type node within one catalogue
     graph".
   - `varve:EventFieldShape`, targeting `varve:EventField`: the node is an
     IRI whose only `rdf:type` is `varve:EventField`; exactly one
     `varve:fieldName`, an `xsd:string` matching
     `^[A-Za-z_][A-Za-z0-9_]{0,63}$` and not `"event"`; exactly one
     `varve:fieldDatatype`, an IRI; exactly one `varve:fieldRequired`, an
     `xsd:boolean`; at most one `varve:fieldAs`, `"string"`; at most one
     `varve:fieldKeepLanguage`, an `xsd:boolean`; at most one `rdfs:comment`,
     an `xsd:string`; and exactly one type node naming it by
     `varve:eventField`.

   The plan's draft said field names would be unique within a type by
   `sh:uniqueValuesFor`. That does not hold: §7.9.5 compares a value node
   with *every target node of the shape*, so on the field shape it would
   forbid two types from each having a field named `id`. Core has no
   component scoped to "the fields of one type", so the rule moves to point 3.
   `fieldDatatype` is an IRI here, as in the code: §5.2's table is applied
   when an event is rendered, not when a declaration is read.
3. **What the shapes cannot state**, and the code alone checks: that the
   `SELECT` parses and projects exactly the fields and the subject; that the
   subject variable is not a field; that field names are unique within one
   type (point 2); that the graph holds no blank node anywhere, no unknown
   `varve:` term, and no node that is neither a type nor a field (Core cannot
   target every subject, nor every predicate in a namespace); that a short
   name is not taken across catalogue graphs; that a graph is the catalogue
   of its source graph; and the version history across positions (ADR 0145).
   Each needs SPARQL parsing, a whole-graph scan, or more than one graph.
   Where both flag a class taken twice, the code keeps the first type and the
   shapes report both; the graph is refused either way.
4. **The agreement property.** In `tests/Varve.Events.Tests`, over generated
   catalogue graphs, valid and with one fault each: a graph the code reads
   without a problem conforms to the shapes; a graph that does not conform,
   the code reports a problem in; a fault of point 2 makes both refuse, and
   the shapes' results name the node the code names; a fault of point 3 makes
   the code refuse while the shapes conform, which pins the boundary. The pull
   request reports the iteration count.

## Alternatives considered

- **A resource of the server.** Rejected: the server is a host; the shapes
  describe the vocabulary `Varve.Events` owns, and a non-server caller would
  have no way to get them.
- **SHACL-SPARQL to cover point 3.** A SPARQL constraint cannot parse a
  SPARQL string, and the cross-graph checks need graphs the shapes graph does
  not name.

## Consequences

- `Varve.Events` gains one public member and an embedded resource, and the
  vocabulary two terms, `varve:EventTypeShape` and `varve:EventFieldShape`,
  listed in `docs/vocabulary.md`.
- The shapes are versioned with the vocabulary: a change to ADR 0144's
  vocabulary changes them, and the agreement property says whether they still
  agree.

## Checks

- **Checked against the accepted ADRs** (0001–0133, 0143–0147). Touches
  **0144** (the vocabulary) and **0145** (what stays the code's). No conflict.
- **Layer ownership.** `Varve.Events`, layer 4; the test references
  `Varve.Shacl` and declares no layer.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-25 — the shapes decide at runtime

Filed by SHACL, part 2b (milestone 8, #13), unaccepted until the maintainer
accepts it (ADR 0066).

Point 1's "`Varve.Events` does not reference `Varve.Shacl`" no longer holds.
Under ADR [0175](0175-shapes-validate-varves-declarations.md),
`EventDeclarationCheck.Check` validates each touched catalogue graph by these
shapes, and their results are its `event-declaration-invalid` problems. The
code checks only point 3. Point 4's property stands, and a second one pins
the runtime against the code check as it stood, frozen in the tests as the
oracle.

**The ledger.** The rulings are ADR 0175's `ShapesAreTheRuntimeValidator`,
`ResidualCheckInCode` and `CodeValidatorsAreTheOracle`.
