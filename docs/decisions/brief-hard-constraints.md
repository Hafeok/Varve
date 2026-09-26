---
set: brief-hard-constraints
namespace: varve
origin: "docs/brief.md, Hard constraints; filed in session 2 of #43"
decisions:
  - key: AllocationPerQuadIsADefect
    statement: "Parsers, the term dictionary and index scans use spans, memory, pipelines, ref structs and pooled buffers, and allocation per quad is a defect"
---

# The brief's hard constraints

The brief (`docs/brief.md`, *Hard constraints*) is the authority every ADR is
checked against, and it is not itself an ADR, so enumerating the ADRs into
decision sets (ADR 0062) left its constraints with no key to cite. ADR 0064
says a `[HotPath]` mark cites "the ledger entry for constraint 5's allocation
rule". There was none, so session 2 of #43 files it here.

Only constraint 5 is filed, because only it is cited today. The other five are
filed when code first needs to cite one.

**Unaccepted.** ADR 0001 treats a matter the brief already settles as accepted,
but a session never writes `accepted-by` (ADR 0066): the maintainer's
acceptance is a signed human commit on the pull request's branch. Until then
every `[HotPath]` mark citing it is `CS0618`.
