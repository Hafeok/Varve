# 0166 — `Varve.Management` and the management vocabulary

## Status

**Accepted — filed unaccepted by milestone Management plane of #105,
2026-10-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided on the plan of #105, questions 2 and 5.

**Amended 2026-10-20** (ADR 0068), by SHACL part 2a (#138), under ADR 0170:
a shapes binding's graphs and mode. **Amended 2026-10-23**, by milestone
Management plane of #105, part 2, under ADR
[0167](0167-event-declarations-move-to-the-management-plane.md): the event
declarations; see the end.

## Context

ADR 0163 puts grants, settings and placement in the system dataset as TriG.
Something must:
- read those declarations;
- check them;
- turn a caller into graph scopes at a position;
- diff desired declarations against current ones.

That something must not pull the system dataset into a lower layer. The
filtered quad source stays in `Varve.Rdf` (ADR 0107), and `Varve.Store` stays
free of grants.

## Decision

1. **`Varve.Management`, layer 4.**
   - **What it references:** `Varve.Iri` (0), `Varve.Rdf` (1) and
     `Varve.Turtle` (2, for TriG). It references neither `Varve.Store` nor
     `Varve.Events`: same-layer references are refused.
   - **What it reads.** It reads declarations from any `IQuadSource`. It never
     sees a dataset: the host pins the system dataset at a position and hands
     the source over.
   - **What it holds:**
     - the declaration model;
     - `DeclarationReader`;
     - `DeclarationValidator`;
     - `GrantTable`, compiled once per management position and immutable;
     - `Caller`, a plain value: agent IRI, role values, group values and
       single-valued claims, with no `ClaimsPrincipal`;
     - `ManagementPlan`.
   - **What it answers.** `GrantTable.ScopesOf(Caller, dataset)` answers a
     `CallerScope` of ADR 0107's types. The governor rule is composed by the
     host, as before.
2. **Graphs of the system dataset.**
   - `varve:manage/fleet`;
   - `varve:manage/dataset/{enc(name)}`, where `enc` percent-encodes with
     upper-case hex, as for the catalogue graphs;
   - `varve:manage/bootstrap`, written only by the system writer and owned by
     no `apply`.
3. **The vocabulary,** all in `https://w3id.org/varve/ns#`.

   **Datasets and settings:**

   | Term | Meaning |
   |---|---|
   | `varve:Dataset` | The class of a dataset declaration. |
   | `varve:datasetName` | A literal that must equal the graph's `{name}`. |
   | `varve:storage` | `varve:File`, `varve:Memory` or `varve:ObjectStorage`. |
   | `varve:placedOn` | A literal instance name (ADR 0163 point 12). |
   | `varve:defaultAccessScope` | `varve:AllHistory` or `varve:Current` (ADR 0021). |
   | `varve:automaticHandover` | A boolean, default false; it is recorded here and acted on by Replication. |
   | `varve:closed` | A boolean, default false: the dataset's state. Every instance closes a closed dataset, and only a closed one may be deleted (ADR 0163). |
   | `varve:erasureMode` | Reserved, and refused until #14. |

   **Grants and principals:**

   | Term | Meaning |
   |---|---|
   | `varve:Grant` | The class of a grant. |
   | `varve:grantee` | One principal. |
   | `varve:permission` | `varve:read`, `varve:write`, `varve:admin`, or `varve:operator` (fleet graph only). |
   | `varve:allGraphs` | `true`. |
   | `varve:graph` | An IRI, or `varve:defaultGraph`; repeatable. |
   | `varve:graphPrefix` | A literal; repeatable. |
   | `varve:graphTemplate` | A `varve:GraphTemplate`; repeatable. |
   | `varve:Agent` | The node is the agent IRI itself. |
   | `varve:Role` | A principal with `varve:claimValue`, matched against the server's role claim. |
   | `varve:Group` | A principal with `varve:claimValue` and `varve:claimType`, default `groups`. |

   **Graph templates:**
   - A `varve:GraphTemplate` has `varve:template`, an RFC 6570 level 1
     string, and `varve:match`, either `varve:Exact` or `varve:Prefix`.
   - Its variables are `{sub}`, `{agent}` and `{claim.<name>}` for a
     single-valued string claim.
   - Each value is percent-encoded to RFC 3986 unreserved characters.
   - The expansion must be an absolute IRI.
   - A caller without the variable gets nothing from the grant.
   - On behalf of a user (ADR 0127), the token's subject is the user, so
     `{sub}` names them.

   **Bindings:**
   - `varve:ShapesBinding` and `varve:TBoxBinding` each have
     `varve:boundGraph`. They are recorded now. Their effect belongs to SHACL
     gating and to Inference.
   - **Room for #14:** the abstract `varve:Binding`, and the reserved
     `varve:ClassifierBinding` and `varve:KeyStoreBinding`. They are refused
     as reserved, and no properties are defined for them.

   **Event schemas** are ADR 0144's terms and join in part 2 (ADR 0167). Until
   then the validator refuses them in a management graph.
4. **The code validator.**
   - **It refuses:**
     - a blank node;
     - a graph that is not one of point 2's;
     - a `varve:datasetName` that does not match its graph;
     - a grant without exactly one grantee and one permission, or without a
       graph set;
     - `admin` with a graph set;
     - `operator` outside the fleet graph;
     - a template that does not parse, names an unknown variable or cannot
       expand to an absolute IRI;
     - a `varve:graph` that is neither an absolute IRI nor `varve:defaultGraph`;
     - a reserved class;
     - any `varve:` term this ADR does not define.
   - **Each finding names its focus node and its path.**
   - **Over HTTP, a failure is `422 declaration-invalid`,** with the findings
     as a member.
   - **It runs** in the CLI before a plan, and on every commit to the system
     dataset as a dataset validator (ADR 0058).
5. **Grants.**
   - **The readable set** is the union of the graph sets of the `read` and
     `write` grants whose principal matches the caller. **The writable set**
     is the union of the `write` grants' graph sets. That is ADR 0107's rule,
     with templates expanded per caller.
   - `admin` and `operator` are dataset-wide and fleet-wide.
   - A `GrantTable` is built once for each position the host applies, and a
     request reads it with one volatile read.
   - The `all` path builds no wrapper (ADR 0107 point 4).
6. **The plan.**
   - `ManagementPlan.Compute(desired, current, versions)` canonicalises both
     sides with RDFC-1.0 (ADR 0059) and diffs them as canonical N-Quads, per
     owned graph.
   - A plan carries the system position and each owned graph's version, and
     is saved as JSON.
   - Applying it sends each owned graph's whole content, so the commit
     replaces the graph exactly.

## Alternatives considered

- **Grant evaluation in `Varve.Server`,** as `GrantedScopes` is today. The CLI
  needs the same model for `--plan`, and a test of grants would need a
  server.
- **In `Varve.Protocol`.** Layer 5 is low enough, but the protocol would then
  have two reasons to change.
- **Blank nodes allowed, canonicalised away.** The store gives a blank node a
  fresh identity at the boundary (ADR 0098), so the replacement would retract
  and assert it on every apply, and the second plan would never be empty.

## Consequences

- **A new package.** The README's package table and `AGENTS.md`'s layer table
  gain it. It takes no new dependency.
- **`GrantedScopes` and `Auth:Datasets` leave the server** (ADR 0163).
- **`docs/vocabulary.md`** gains the terms.

## Checks

- **Checked against the accepted ADRs** (0001–0147). It touches:
  - **0003** (a new layer-4 package);
  - **0059** (RDFC);
  - **0098** (no blank nodes);
  - **0107** (the scope types reused);
  - **0127** (templates over the subject).

  No conflict.
- **Layer ownership.** `Varve.Management` (4), as above.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-20 — a shapes binding's graphs and mode

Filed by SHACL, part 2a (#138, under milestone 8, #13), unaccepted until the
maintainer accepts it (ADR 0066).

A `varve:ShapesBinding` is no longer only recorded: ADR
[0170](0170-the-shapes-binding-vocabulary.md) gives it effect.
- **Its terms.** `varve:boundGraph` names its shapes graph. It carries its
  data graphs in point 3's graph-set terms (`varve:allGraphs`, `varve:graph`,
  `varve:graphPrefix`), as a grant does. It has a `varve:validationMode` of
  `varve:Reject`, `varve:Report` or `varve:Off`, and may set
  `varve:allowFullEvaluation` and `varve:subClassOfInShapesGraph`.
- **What point 4's validator refuses**, beside what it refused: a shapes
  binding without exactly one mode or without a graph set; with a
  `varve:graphTemplate`; or naming its own shapes graph by `varve:graph`.
- `varve:TBoxBinding` is unchanged, recorded for Inference.

**The ledger.** The rulings are ADR 0170's `BindingTerms` and
`BindingDeclarationChecks`.

## Amendment, 2026-10-23 — the event declarations join

Filed by milestone Management plane, part 2, of #105, unaccepted until the
maintainer accepts it (ADR 0066). The rulings are ADR
[0167](0167-event-declarations-move-to-the-management-plane.md)'s.

- **Four terms.** `varve:eventDeclarations`, on the dataset node, with
  `varve:Moving` or `varve:Moved`; and `varve:sourceGraph`, an IRI or
  `varve:defaultGraph`, on an event type node.
- **Point 3's "Until then the validator refuses them"** ends for a dataset
  whose graph says `varve:Moved`. There, ADR 0144's terms are known, and the
  event nodes are checked by `EventDeclarationCheck` over their
  catalogue-graph translation (ADR 0145), composed with point 4's validator
  in `ManagementValidator`. In any other dataset's graph, and in the fleet
  graph, they are still refused, with a message that names the move.
- **What point 4's validator refuses**, beside what it refused: more than one
  `varve:eventDeclarations`; a change of it other than none or `InDataset`
  to `Moving`, `Moving` to `InDataset` (a cancel), or `Moving` to `Moved`; and event terms brought in by any
  commit but the one that makes the graph `Moved`, or later ones.

## Amendment, 2026-10-25 — shapes validate the declarations

Filed by SHACL, part 2b (milestone 8, #13), unaccepted until the maintainer
accepts it (ADR 0066).

- **Point 1.** `Varve.Management` also references `Varve.Shacl` (3).
- **Point 4.** The validator refuses what it refused, and its findings keep
  their wire shape. What SHACL Core can state, `shapes/management.ttl` decides
  at runtime, with each finding built from a report result. The code keeps:
  - blank nodes;
  - unknown and reserved terms;
  - the graphs point 2 admits;
  - the dataset named as its graph;
  - the fleet graph's rules and the bootstrap graph;
  - template syntax.

  The validator as it stood is frozen in the tests as the oracle (ADR
  [0175](0175-shapes-validate-varves-declarations.md)).

**The ledger.** The rulings are ADR 0175's `ShapesAreTheRuntimeValidator`,
`ResidualCheckInCode` and `CodeValidatorsAreTheOracle`.

## Amendment, 2026-10-30 — the classifier binding and subject-scoped grants

Filed by milestone 9, part 1 (#202, under #14), unaccepted until the maintainer accepts it (ADR 0066). The rulings are ADR [0234](0234-subject-scoped-grants-and-the-classifier-binding.md)'s.

- **Point 3.**
  - `varve:ClassifierBinding` is no longer reserved. It has one
    `varve:boundGraph` and a graph set.
  - A grant gains `varve:subject`, `varve:subjectPrefix`,
    `varve:subjectTemplate` (a `varve:SubjectTemplate`), `varve:subjectClass`,
    `varve:subjectLink` (a `varve:SubjectLink` with `varve:linkPredicate` and
    `varve:linkTarget`, which is `varve:CallerAgent` or a template), and, on
    a write grant, `varve:createUnder`.
  - `varve:KeyStoreBinding`, `varve:erasureMode` and `varve:privacy` stay
    reserved for #203.
- **Point 4's validator** refuses, beside what it refused, ADR 0234's P1 to
  P5 and its other refusals.
- **Point 5.** `GrantTable.ScopesOf` answers a `RowScope` per classifier
  binding, readable and writable.
- **Point 6.** The plan names row restrictions, and says when an apply widens
  one.

`BindingsReservedForErasure` moves to ADR 0234's set, superseded.
