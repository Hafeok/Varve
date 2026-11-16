# PositionStamp

A log position, labelled with what it is. The commonest element in the console: on every view head, in every commit row, in every problem that names a position.

- **Consumer provides:** the label and the integer position. The label names the log the position is in, so two logs on one screen are never read as one (review F05): `notes position 18417`, `management position 412`, and, where the position has a role, the role first: `as of notes position`, `expected management position`. The atom stays generic: it prints the label it is given, and the parts and pages name the log.
- The value is `code`, no thousands separator, `user-select: all`, so one click selects exactly what pastes into `Varve-As-Of` or the CLI.
- `vw-position` (on `layer`) is a data-plane position. `vw-position--management` (outlined) is a position of the system dataset, so the two are never confused side by side.
