# Field

A labelled text input with help and error lines. The console has one use in part 1, typing a dataset's name to confirm closing it; part 2's query and as-of inputs reuse it.

- **Consumer provides:** a stable `id`, the label (sentence case, naming what to enter), optional help, and an error message when the value is refused. The error sets `aria-invalid="true"` on the input and is referenced by `aria-describedby`, as is the help.
- The input is set in `code` because what is typed is always a name, an IRI or a position: values that must match exactly.
- Errors say what is wrong and what is expected ("Type notes exactly; the name is case-sensitive"), never only "Invalid".
- `readonly` renders on `surface-sunk` for a value shown for copying.
