---
set: the-validation-projection
namespace: varve
adr: 0174
decisions:
  - key: OneProjectionPerBinding
    statement: "Each shapes binding not off has one IProjection in Varve.Shacl.Store, fed the dataset's closed commits in order, whose rows are keyed by (targeted shape, focus node), each holding that pair's results with a validity interval [asserted, retracted)"
  - key: PairsMaintainedByTheComparison
    statement: "A data commit replaces exactly the rows of the pairs its delta affects, by ADR 0171's comparison; every other row keeps its interval open"
  - key: GenerationsSwappedWhole
    statement: "A shapes change at position s, or a shapes graph that failed before, starts a generation: a full validation at s that replaces every open row, published only when complete, so a reader never sees a mixed report"
  - key: ReportAtEqualsFullValidation
    statement: "The report at p is the composition of the rows valid at p, and equals ShaclValidator.Validate over AsOfAsync(p) through the binding's data graph, byte for byte in canonical N-Quads"
  - key: LocalCacheOnly
    statement: "The projection's state is stored under derived/validation/{enc(binding)}/ in an IDerivedStore the host supplies from the local cache, never on an object store, and every instance rebuilds it on a cold start"
  - key: SnapshotIdentifiesItsBinding
    statement: "A snapshot in the local cache records its binding's IRI, shapes graph, graphs, IRI prefixes as strings, default graph and subclass flag under a format version, and is read only when every one matches; a release that changes SHACL results bumps the format, and otherwise the projection is rebuilt from the log"
  - key: ReservedReportGraph
    statement: "The report at a position is readable as the reserved graph https://w3id.org/varve/ns#validation/{enc(binding IRI)}, composed at a pinned position through the staging view by a Varve.Protocol seam the host implements"
  - key: ReadNeedsEveryValidatedGraph
    statement: "A caller reads a binding's report graph only when it reads every graph the binding validates, since the report names their nodes and values (ADR 0107, amended)"
  - key: ReportGraphNotWritable
    statement: "A commit with a quad in a graph under https://w3id.org/varve/ns#validation/ is refused as graph-not-writable on every write path, admin or not (ADR 0130 point 5, amended)"
  - key: ValidationBehind
    statement: "A read of a report graph at a position above the projection's, after one bounded wait, is refused with 503 validation-behind naming the binding, the position and the projected position"
  - key: LagReportedNeverUnready
    statement: "Readiness reports each binding's lag beside its dataset's line, and a projection's lag never makes the dataset unready or behind"
---

The rulings of [ADR 0174](../adr/0174-the-validation-projection.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 2b (#13) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
