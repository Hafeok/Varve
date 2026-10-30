# TripleList

The statements of a description, a commit's delta or a diff between two positions: a table, with a header cell naming each column, and one `Triple` row per statement. It is tabular data, so it is a real `<table>` (review F27): assistive technology reads each term with its column.

- **Consumer provides:** a caption naming the statements ("The changes of commit 18417"), visually hidden, and the rows, each a `Triple`, already sorted (in a diff, retractions before assertions for the same subject and predicate).
- **The header cells** are `<th scope="col">`: Change (visually hidden, over the sign column), Subject, Predicate, Object and Note. Below 640px the Note column is hidden.
- **Empty** renders the header and an empty body. The page says why there is nothing above it (a commit with no statements, or a position with no description); the list never invents a sentence.
- An organism: it renders its rows from its content slot and composes no `Triple` itself, because the page builds the rows.
