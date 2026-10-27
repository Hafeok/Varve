# 0175 — Shapes validate Varve's own declarations

## Status

**Accepted — filed unaccepted by SHACL, part 2b (milestone 8, #13),
2026-10-25** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer:
- on the part 2 plan (#13, 2026-10-20): shapes become the runtime validator
  of event declarations (ADR 0157's shapes) and of management declarations.
  The code validators stay in the tests as the oracle of agreement
  properties, and the wire shapes are kept;
- on the part 2b plan (#13, 2026-10-23, answered 2026-10-25): **Core
  shapes and residual code.** What SHACL Core cannot state stays in code beside the shapes,
  rather than in SPARQL constraints, so neither package references
  `Varve.Shacl.Sparql`.

Amends **0157** point 1 (`Varve.Events` now references `Varve.Shacl`),
**0166** point 1 (`Varve.Management` references `Varve.Shacl`) and point 4
(the validator's checks), and **0145** point 4 (what the commit-time check
runs), each by a dated amendment.

## Context

ADR 0157 wrote shapes for event declarations and proved, by an agreement
property, that they agree with the code on everything they state. The code
still decided at runtime. Management declarations (ADR 0166, 0170) had no
shapes. Milestone 8 closes by making Varve's own declarations the first
users of its SHACL: what a declaration may say is then stated once, in a
graph a user can read, and the same processor checks it that checks the
user's data.

## Decision

1. **The shapes are the runtime validator**, behind the existing entry
   points:
   - `EventDeclarationCheck.Check`, which `EventCatalogueValidator` runs on
     a commit, validates each touched catalogue graph by
     `EventDeclarationShapes.ttl` (ADR 0157 point 2). The event shapes'
     results become `event-declaration-invalid` problems.
   - `DeclarationReader.Read`, which `Varve.Protocol`'s
     `ManagementValidator` runs on every commit to the system dataset,
     validates each fleet and dataset graph by
     `src/Varve.Management/shapes/management.ttl`. That file is embedded in
     the assembly and readable through `ManagementShapes.Turtle`, and its
     results become `declaration-invalid` findings.

   ADR 0166 names the management side's validator `DeclarationValidator`.
   No type has that name: the validation is `DeclarationReader`'s, and
   `ManagementValidator` is its commit-time host.

   The management shapes state, by SHACL Core only:
   - `varve:Dataset`: its name, storage, default access scope, description
     and flags, each at most once and of its type, and where its event
     declarations are (`varve:eventDeclarations`, ADR 0167): at most once,
     `varve:InDataset` (as a cancel states it), `varve:Moving` or
     `varve:Moved`, on the dataset's node only;
   - a moved `varve:EventType`: exactly one `varve:sourceGraph`, an IRI or
     `varve:defaultGraph`;
   - `varve:Grant`: exactly one IRI grantee and one permission; an
     `admin` or `operator` grant names no graph set, and a `read` or `write`
     grant names one;
   - a grant's or shapes binding's graph set: `varve:allGraphs` alone or
     named graphs, prefixes and templates, each well typed, a template
     declared in the same graph;
   - a grantee: a role or a group, not both, with its claim value, and a
     group's claim type at most once;
   - a `varve:GraphTemplate`: its template string and match mode;
   - a binding: exactly one bound graph. A shapes binding has exactly one
     mode and a graph set, no template, and does not name its own shapes
     graph.

   Each finding is built from a report result. Its node is the result's
   focus node, its property the result's path, and its message the shape's
   `sh:message`.
2. **The event shapes target by class.** They validate a declaration in a
   catalogue graph and in a management graph alike, wherever Management
   plane part 2 moves it. Both homes are tested, and so is Management plane
   part 2's fixture of moved declarations
   (`tests/Varve.Events.Tests/fixtures/moved-declarations/`), unchanged and
   with each fault the shapes state:
   - in its management graph, the shapes name the faulty node;
   - in catalogue form, the code names it, and the runtime check and its
     oracle agree.

   Management plane part 2 (#147) landed first, and this pull request
   reconciled `shapes/management.ttl` with the move's terms, as point 1
   lists them.
3. **The residual check in code** is what Core cannot state:
   - **for events**, ADR 0157 point 3 and the version history (ADR 0145);
   - **for management declarations**:
     - blank nodes;
     - unknown and reserved `varve:` terms;
     - ADR 0144's event-schema terms only in a dataset's graph that says
       `varve:eventDeclarations varve:Moved`, which is the graph's own state;
     - the move's transitions (ADR 0167 point 4), which compare two
       management positions and stay `ManagementValidator`'s;
     - the graph names the system dataset admits;
     - one `varve:Dataset` per dataset graph, named as its graph;
     - the fleet graph's rules;
     - the bootstrap graph's form;
     - the template syntax.

   Each check needs the graph's name, a count across the graph, a scan of
   every predicate, or a parser.
4. **The oracle.** The code validators as they stood before the switch are
   frozen in the test projects, as `Oracle.CodeEventDeclarationCheck` and
   `Oracle.CodeDeclarationReader`. The latter carries #147's additions to
   the reader as #147 made them, and the `varve:sourceGraph` rule its move
   translation (`ManagedEventDeclarations`) checked. Over generated declarations, valid and
   mutated by one fault each, the runtime refuses exactly what the oracle
   refuses, with the same problem type. For an incompatible change, it also
   gives the same declaration and fields. For a fault the shapes state, the
   shapes name the node the code names. Each property was checked by
   sabotage: dropping the shapes' findings at runtime fails it.
5. **The wire shapes are kept.** `declaration-invalid`,
   `event-declaration-invalid` and `event-schema-incompatible` keep their
   members. A message is the shape's `sh:message`, which keeps the code's
   wording where the rule is the same.

   Two refusals the move's terms bring change where they are reported:
   - **A moved type's `varve:sourceGraph`.** Missing, doubled or a literal,
     it is refused at commit time by the shapes as `declaration-invalid`,
     before `ManagementValidator` translates the moved nodes. Under #147
     alone, the translation refused it as `event-declaration-invalid`. The
     message is the same, and the translation keeps its check for the move's
     own copy.
   - **A stray `varve:eventDeclarations` in the fleet graph** is refused,
     where the code read only dataset graphs for it.

## Alternatives considered

- **SPARQL constraints for the residual checks.** Rejected on the plan:
  both packages sit at layer 4, beside `Varve.Shacl.Sparql`. Several checks
  need the graph's name, which a constraint does not have.
- **Shapes only in the tests**, as ADR 0157 left them. The code would stay
  the definition, and a user could not read what Varve checks.
- **Deleting the code validators.** Without them nothing would pin that the
  switch changed no verdict.

## Consequences

- **Every catalogue or management commit runs a SHACL validation** of the
  touched graphs. They are small, and the shapes load once per process.
- **A rule that moves between shapes and code** moves with its agreement
  test, since the oracle is frozen.
- **Management plane part 2** moves event declarations into management
  graphs. The event shapes need no change for it. Whichever lands second
  merges main.

## Checks

- **Checked against the accepted ADRs** (0001–0174). Touches:
  - **0003** and **0060**: layer 4 referencing layer 3, as allowed;
  - **0145**, **0157**, **0166** and **0170**: amended or applied as above;
  - **0119**: the problem types, unchanged.

  No conflict.
- **Layer ownership.** `Varve.Events` (4) and `Varve.Management` (4)
  reference `Varve.Shacl` (3).
- **Analyzer rule.** None.
- **Open questions owned.** None.
