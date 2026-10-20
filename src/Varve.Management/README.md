# Varve.Management

The management plane's declarations (ADRs 0163 and 0166). A fleet of Varve
servers keeps its datasets, settings, placement and grants as TriG in a system
dataset: one graph per dataset, `varve:manage/dataset/{name}`, and one for the
fleet, `varve:manage/fleet`. This package reads them, checks them, evaluates
grants for a caller, and computes the plan `varve apply` converges. It knows no
store and no HTTP: a host pins the system dataset at a position and hands its
quads over.

- **`DeclarationReader`** reads a quad source into `Declarations`, with the
  code validator's findings: each names its graph, its focus node and its path.
- **`GrantTable`** compiles the grants of one management position. It answers
  a `Caller`'s `CallerScope` on a dataset: the union of its read and write
  grants, templates over the caller expanded, or everything for an admin or an
  operator.
- **`GraphTemplates`** expands RFC 6570 level 1 templates over `{sub}`,
  `{agent}` and `{claim.<name>}`, so that on-behalf-of users own their graphs.
- **`ManagementPlan`** is the per-graph diff of canonical N-Quads after
  RDFC-1.0, with the system position and each owned graph's version, saved
  as JSON.
- **`ManagementTrig`** reads and writes declarations as TriG.

Event-schema declarations join the vocabulary with ADR 0167; until then they
are refused in a management graph.
