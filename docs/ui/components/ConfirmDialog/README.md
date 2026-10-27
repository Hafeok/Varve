# ConfirmDialog

The confirmation every act passes through: the dataset and the effect named in the title and body, the drift note when the act changes a declaration, and, for closing a dataset, the dataset's name typed to confirm.

- **Consumer provides:** the act's title ("Close notes?"), one sentence naming its effect on the fleet, whether the drift note applies, and whether a typed name is required (close only).
- The confirming button repeats the act and the name ("Close notes"); it is enabled only when the typed name matches exactly.
- After the write, the dialog shows the result in place: the new position on success, or the `ProblemPanel` (a `409` or `412`) with its reload action. It never closes on a failure.
- A native `<dialog>` opened modal; Escape and Cancel close it without acting.
- An organism: it composes `Field`, `Notice`, `Button` and, on a failed write, `ProblemPanel`. It renders no input styles of its own.
