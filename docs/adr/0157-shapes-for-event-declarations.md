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
2. **What the shapes state**, by SHACL Core only:
   - a node typed `varve:EventType` is an IRI, with exactly one
     `varve:eventName` matching the short-name pattern, exactly one
     `varve:eventClass` IRI, exactly one positive `varve:eventVersion`, exactly
     one `varve:eventSelect` string, at most one `varve:eventSubject` and
     `varve:eventRetired`, and `varve:eventField` values conforming to the
     field shape;
   - a node typed `varve:EventField` is an IRI with exactly one
     `varve:fieldName` matching the field-name pattern and not `event`, at most
     one each of the optional field properties with their datatypes, and is
     the field of exactly one type;
   - field names are unique within one type (`sh:uniqueValuesFor`, SHACL 1.2
     §7.9.5), and no node is both a type and a field.
3. **What the shapes cannot state**, and the code alone checks: that the
   `SELECT` parses and projects what the fields name; that the subject
   variable is a variable of the `SELECT` and not a field; that an event class
   or a short name is not taken by another catalogue graph; that a graph is
   the catalogue of its source graph; and the version history across positions
   (ADR 0145). Each needs SPARQL parsing or more than one graph.
4. **The agreement property.** In `tests/Varve.Events.Tests`, over generated
   declarations, valid and invalid: every declaration the code accepts conforms
   to the shapes; every declaration the shapes reject, the code rejects; and on
   the problems both can see — those of point 2 — they reject the same
   declarations. The pull request reports the iteration count.

## Alternatives considered

- **A resource of the server.** Rejected: the server is a host; the shapes
  describe the vocabulary `Varve.Events` owns, and a non-server caller would
  have no way to get them.
- **SHACL-SPARQL to cover point 3.** A SPARQL constraint cannot parse a
  SPARQL string, and the cross-graph checks need graphs the shapes graph does
  not name.

## Consequences

- `Varve.Events` gains one public member and an embedded resource.
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
