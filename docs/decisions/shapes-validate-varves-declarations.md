---
set: shapes-validate-varves-declarations
namespace: varve
adr: 0175
decisions:
  - key: ShapesAreTheRuntimeValidator
    statement: "Event and management declarations are validated at runtime by SHACL Core shapes the vocabularies' packages ship and reference Varve.Shacl for — ADR 0157's event shapes and Varve.Management's shapes/management.ttl — behind the existing entry points EventDeclarationCheck.Check and DeclarationReader.Read; each finding is built from a report result"
  - key: ResidualCheckInCode
    statement: "What SHACL Core cannot state stays a residual check in code: for events ADR 0157 point 3 and the version history; for management declarations blank nodes, unknown and reserved varve: terms, ADR 0144's terms only in a dataset's graph that says varve:eventDeclarations varve:Moved, the move's transitions between management positions, the graph names the system dataset admits, one varve:Dataset named as its graph, the fleet graph's rules and the template syntax"
  - key: CodeValidatorsAreTheOracle
    statement: "The code validators as they stood before the switch are frozen in the test projects as the oracle of agreement properties over generated declarations, valid and mutated: the runtime refuses exactly what the oracle refuses, and the shapes name the node the code names"
  - key: WireShapesKept
    statement: "declaration-invalid, event-declaration-invalid and event-schema-incompatible keep their wire shape; a finding's message is the shape's sh:message, which keeps the code's wording where the rule is the same"
  - key: TargetedByClassInEitherHome
    statement: "The event shapes target event nodes by class, so they validate a declaration in a catalogue graph and in a management graph alike, where Management plane part 2 moves it"
---

The rulings of [ADR 0175](../adr/0175-shapes-validate-varves-declarations.md), one line each. The ADR
is the narrative; this is what code cites. Filed unaccepted by SHACL, part 2b (#13) (ADR 0066);
every citation is `CS0618` until the maintainer accepts them.
