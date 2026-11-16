# Row-level access

Grants decide which **graphs** a caller reads and writes (`fleet.md`). Beneath
graphs, a **classifier binding** decides which **rows** belong to which data
subjects, and a grant with **subject terms** limits its grantee to the rows of
its subjects. Row-level access works with erasure mode off and is stable from
1.0. The ADRs are 0230–0238.

## The model in one paragraph

A **row** is every quad of a classified graph with the same subject node. The
classifier names some nodes **data subjects** (a tenant, a person). A node
belongs to the subjects it is, and to those its **links** reach: a space
linked to a lease, linked to a tenant, belongs to the tenant. A quad is about
its subject's subjects and, unless its predicate is a **public link**, its
object's. A caller sees the quad when its graph is readable and every subject
the quad is about is in the caller's subject set. A quad about no subject, or
in a graph no binding classifies, is governed by its graph alone.

## Declaring a classifier binding

The binding names an annotated shapes graph of the dataset and the graphs it
classifies, in the dataset's management graph:

```trig
<https://w3id.org/varve/ns#manage/dataset/portfolio> {
  ex:rows a varve:ClassifierBinding ;
    varve:boundGraph <https://example.org/shapes/rows> ;
    varve:graphPrefix "https://example.org/graph/site/" .
}
```

The shapes graph says who the data subjects are, which predicates carry
ownership, and which predicates are public:

```turtle
ex:TenantShape a sh:NodeShape ;
  sh:targetClass rec:Tenant ;
  varve:dataSubject true .

ex:LeaseTenant a sh:PropertyShape ;
  sh:path ex:leasedBy ;           # a lease belongs to its tenant
  varve:subjectLink true .

ex:SpaceLease a sh:PropertyShape ;
  sh:path ex:coveredBy ;          # a space belongs to its lease's tenant
  varve:subjectLink true .

ex:SpaceKind a sh:PropertyShape ;
  sh:path ex:spaceKind ;          # a vocabulary IRI, not anyone's row
  varve:publicLink true .
```

- **Targets** are `sh:targetClass` (and a shape that is itself a class),
  `sh:targetNode` and `sh:targetSubjectsOf`. Any other target on an
  annotated shape is refused, because it would read beyond the focus's row.
- **`rdf:type` is always public.**
- **A shapes graph that does not classify fails closed.** That covers a refused
  target, a predicate both linking and public, or `varve:privacy` (reserved for
  erasure mode). A row-restricted caller then sees nothing of the binding's
  graphs until the shapes are repaired; unrestricted callers are unaffected.
  `/health/ready` reports the binding's state.

## Granting rows

Subject terms go on an ordinary read or write grant:

```trig
ex:tenant-users a varve:Grant ;
  varve:grantee ex:TenantUsers ;
  varve:permission varve:write ;
  varve:allGraphs true ;
  varve:subjectTemplate ex:own-tenant ;
  varve:createUnder ex:own-tenant-items .

ex:own-tenant a varve:SubjectTemplate ;
  varve:template "https://example.org/tenant/{claim.tenant}" ;
  varve:match varve:Exact .

ex:own-tenant-items a varve:SubjectTemplate ;
  varve:template "https://example.org/tenant/{claim.tenant}/" ;
  varve:match varve:Prefix .
```

| Term | The subjects |
|---|---|
| *(none)* | every subject: today's grants mean what they meant |
| `varve:subject <iri>` | named ones |
| `varve:subjectPrefix "…"` | IRIs under a prefix |
| `varve:subjectTemplate` | a template over the caller: `{sub}`, `{agent}`, `{claim.<name>}` |
| `varve:subjectClass <C>` | data subjects of class `C` |
| `varve:subjectLink [ a varve:SubjectLink ; varve:linkPredicate ex:caseworker ; varve:linkTarget varve:CallerAgent ]` | data subjects whose `ex:caseworker` is the caller |
| `varve:createUnder` | where a write grant may create new rows |

`varve apply` refuses, naming the grant and the binding:
- a grant with subject terms that does not cover **every** graph of a binding
  it meets, or that names its graphs by template;
- a grant with subject terms that meets no binding;
- two classifier bindings over a graph in common;
- a `reject` or `report` shapes binding over classified graphs while a
  row-restricted write grant covers them, because its refusal could depend on
  another row.

## Writing under a row restriction

A row-restricted writer writes only rows that are already wholly its own.
Every breach is the same `403 row-not-writable`, which names nothing (ADR
0236):
- the quad's subject node must belong to the writer's subjects, before and
  after;
- a **new** row is created only at a fresh blank node or at an IRI under
  `varve:createUnder`. Any other IRI is refused whether or not something is
  there, so a write cannot probe for a hidden row;
- an object IRI under a predicate that is not public must have a row the
  writer reads. **Links to IRIs outside the binding's graphs** (an external
  vocabulary, a code list) need their predicate declared `varve:publicLink`;
- unclassified rows are read-only to a row-restricted writer;
- deleting a quad the writer does not see is a no-op.

## What a row-restricted caller sees elsewhere

- **The feed and the diff** deliver the caller's view: a quad that becomes
  visible or hidden because a node was reclassified is delivered as an
  addition or a retraction, though no quad changed. Each delivered commit
  reads two as-of views; budget for it on busy feeds.
- **Validation report graphs** are not readable, and commits carry no report
  attachment for such a caller.
- **Inference**: rewriting (`Varve-Entailment: rdfs`) is served over the
  caller's rows. The materialised closure is not (ADR 0238), pending #171.
- **A readable graph whose quads are all hidden** is an empty graph, not a
  `404`.
- **A graph no binding classifies** is governed by its graph alone. A
  subject's IRI there is shown to every reader of that graph. To hide a
  subject's IRI everywhere, classify every graph that can name it.

## The classification projection

Each classifier binding has a projection (ADR 0233), kept like the validation
projection:
- it is saved to a file dataset's `derived/` and verified in full when read,
  so a damaged blob is rebuilt, never served;
- a memory or object-storage dataset keeps it in memory and starts at the
  head;
- its lag is on `/health/ready` as `classification`, and never makes the
  dataset unready;
- **only a row-restricted read waits for it**, once, two seconds, and then
  answers `503 classification-behind` with `Retry-After: 1`;
- a cold start classifies the head once per binding. Reads below that floor
  classify from scratch, at the cost of the as-of state;
- a tier-1 follower keeps its own projection and enforces the same rows at
  its position.

## Access requests without erasure mode

`GET /datasets/{name}/access?subject=<IRI>`, for a dataset admin, answers the
quads about one data subject at the head, as N-Quads. Without erasure mode
the scope is `Current` only. Erasing a subject needs erasure mode (#203).
