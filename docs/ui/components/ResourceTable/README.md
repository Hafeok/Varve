# ResourceTable

The list form of a collection resource: datasets, projections, bindings, grants, fleet instances. Dense, sortable by header, one row per item.

- **Consumer provides:** the columns, rows, and which cell names the row (`vw-table__name`, linked to the item). Numbers go in `vw-num` cells, right-aligned in `code`.
- **States:** a `vw-table__loading` row (three of them) while loading; a `vw-table__empty` row saying what would appear and how it gets there; on a problem, a **Problem** slot the page fills with a `ProblemPanel`, rendered in place of the table. The table never composes `ProblemPanel` itself: both are organisms, and a component uses only lower levels.
- Wrap in `vw-table-wrap` so a wide table scrolls inside itself, never the page. Header cells are real `<th scope="col">`.
