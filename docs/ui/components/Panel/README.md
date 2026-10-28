# Panel

The container a resource renders in: a hairline box on `surface` with an optional head (title, state, actions) and a body.

- **Consumer provides:** a `section-title`, optional badges and buttons in the head, the body content. A `TitleId` gives the title an id, for a table in the panel that takes its name from it (`ResourceTable`). A table goes directly in the panel without `vw-panel__body`, so its rows run edge to edge.
- Panels stack in the main column with `space-6`. No shadow, no coloured edge: a panel is set apart by its rule, and its state is said by a badge in its head.
