# Panel

The container a resource renders in: a hairline box on `surface` with an optional head (title, state, actions) and a body.

- **Consumer provides:** a `section-title`, optional badges and buttons in the head, the body content. A `TitleId` gives the title an id, for a table in the panel that takes its name from it (`ResourceTable`). A table goes directly in the panel without `vw-panel__body`, so its rows run edge to edge.
- **Stale** (`vw-panel--stale`, review F09): under the server-unreachable notice, every panel showing data read before the server stopped answering is drawn dashed and says, under its head, "As last read at `<time>`": the one time of the last successful read, the notice's own. Stale data never looks live.
- Panels stack in the main column with `space-6`. No shadow, no coloured edge: a panel is set apart by its rule, and its state is said by a badge in its head.
