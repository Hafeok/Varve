# TripleList

The statements of a description, a commit's delta or a diff between two positions: a header row naming the columns, and one `Triple` row per statement.

- **Consumer provides:** the rows, each a `Triple`, already sorted (in a diff, retractions before assertions for the same subject and predicate).
- **The header row is pinned:** `vw-triples__head`, with Subject, Predicate, Object and Note over the sign column. It is for sighted readers and is `aria-hidden`, since each row's terms and its sign's label already say what they are. Below 640px it keeps only the sign and the first column, as the rows stack.
- **Empty** renders the header and an empty list. The page says why there is nothing above it (a commit with no statements, or a position with no description); the list never invents a sentence.
- An organism: it renders its rows from its content slot and composes no `Triple` itself, because the page builds the rows.
