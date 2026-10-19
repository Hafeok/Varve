# 0166 — `Varve.Management` and the management vocabulary

## Status

**Accepted — filed unaccepted by milestone Management plane of #105,
2026-10-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided on the plan of #105, questions 2 and 5.

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
