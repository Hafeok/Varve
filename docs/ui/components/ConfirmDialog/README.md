# ConfirmDialog

The confirmation every act passes through: the dataset and the effect named in the title and body, a note when the act needs one (the drift note when it changes a declaration), and, for closing a dataset, the dataset's name typed to confirm.

- **Consumer provides:** the act's title ("Close notes?"), one sentence naming its effect on the fleet, whether a typed name is required (close only), and two slots the page fills:
  - **Note:** a `Notice` shown above the field, such as the drift note, whose wording lives once, in a part (see `Notice`).
  - **Result:** what the write answered, shown in place: the new position on success, or the `ProblemPanel` (a `409` or `412`) with its reload action.
- The confirming button repeats the act and the name ("Close notes"); it is enabled only when the typed name matches exactly.
- **Dismissing:** "Cancel" before the act, "Done" after it. "Close" appears only as the act itself ("Close notes"), never as the way out of a dialog about closing (review F08).
- **The effect is the dataset's own.** Closing a `Memory` dataset says its data is lost: Memory storage keeps nothing once the dataset is closed. Every other storage says nothing is deleted.
- **After a conflict,** "Reload at head" reads the view again and leaves the dialog open as it was filled in, so the act is confirmed against the new version without starting over.
- It never closes on a failure.
- A native `<dialog>` opened modal with `showModal()`, so its markup carries no `open` attribute; Escape and Cancel close it without acting. The preview shows it in place with a preview-only style.
- An organism: it composes `Field` and `Button`, and renders the note and the result from its slots. It never composes `ProblemPanel` or `Notice` itself, because a component uses only lower levels and never its own. It renders no input styles of its own.
