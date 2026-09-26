---
set: sparql-algebra-surfaces
namespace: varve
origin: "DD0013 and DD0016 findings on Varve.Sparql.Algebra in session 2 of #43"
decisions:
  - key: GrammarKeywordsAreBools
    statement: "An algebra node records an optional keyword of the SPARQL grammar, SILENT, DISTINCT, DESC or NOT, as a bool named after the keyword"
  - key: QueryLiteralsKeepTheirTypes
    statement: "A value a query writes literally, a GROUP_CONCAT separator, a prefix label, OFFSET and LIMIT, is carried in the algebra as the string or integer the query text gives"
  - key: AlgebraListCountsAndIndexesAsInt
    statement: "AlgebraList exposes its length and indexer as int, as every .NET collection does"
---

# The primitives on the SPARQL algebra's surface

**Unaccepted.** Filed by session 2 of #43, for the maintainer.

ADR 0064 declares `Varve.Sparql.Algebra` a `[DomainModel]` namespace, which
brings the algebra's public members under `DD0013` and `DD0016`. ADR 0048
made the nodes sealed, immutable records whose positional parameters are the
grammar's parts, and ADR 0065's wrappers do not reach this far. Three
questions remain, each filed with its alternative.

**`GrammarKeywordsAreBools`.** `Load(…, bool Silent)`, `Clear`, `Drop`,
`Create`, `Add`, `Move`, `Copy`, `Service(…, bool Silent)`,
`AggregateExpression(…, bool Distinct, …)`, `OrderCondition(…, bool Descending)`,
`ExistsExpression(…, bool Negated)`. Each bool is one optional keyword. Nodes
are built by the parser and by the optimiser, and the parameter name is the
keyword. The alternative is one two-member enum per keyword (`Silence`,
`Distinctness`, `SortDirection`, `Polarity`), about five new public types.

**`QueryLiteralsKeepTheirTypes`.** `AggregateExpression.Separator` (a string
from `GROUP_CONCAT(… ; SEPARATOR = "…")`), `PrefixDeclaration.Prefix` (a prefix
label) and `Slice.Offset` and `.Limit` (`OFFSET` and `LIMIT`). The alternatives
are wrappers: `PrefixLabel`, `RowCount`.

**`AlgebraListCountsAndIndexesAsInt`.** `AlgebraList<T>.Count` and its indexer.
The alternative is to drop both and expose only `Span`, which the type already
has.

Each member cites its key with `Scope = ExceptionScope.Boundary`.
