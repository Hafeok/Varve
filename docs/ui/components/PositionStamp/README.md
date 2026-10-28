# PositionStamp

A log position, labelled with what it is. The commonest element in the console: on every view head, in every commit row, in every problem that names a position.

- **Consumer provides:** the label (`read at`, `as of`, `head`, `management`, `expected`) and the integer position.
- The value is `code`, no thousands separator, `user-select: all`, so one click selects exactly what pastes into `Varve-As-Of` or the CLI.
- `vw-position` (on `layer`) is a data-plane position. `vw-position--management` (outlined) is a position of the system dataset, so the two are never confused side by side.
