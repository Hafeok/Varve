---
set: shapes-for-event-declarations
namespace: varve
adr: 0157
decisions:
  - key: EventDeclarationShapesInEvents
    statement: "Varve.Events ships SHACL shapes for its declaration vocabulary as an embedded Turtle resource, without referencing Varve.Shacl"
  - key: ShapesAgreeWithDeclarationCode
    statement: "The event declaration shapes and the declaration code agree on generated declarations, by property, on every check the shapes can state"
---

The rulings of [ADR 0157](../adr/0157-shapes-for-event-declarations.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
