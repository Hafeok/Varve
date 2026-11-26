# Menu

A disclosure button that opens a list of actions (buttons, not an ARIA `menu`: no roving focus to get wrong). Escape closes it and returns focus to the button. Parameters: Id, Label, Open, Items (text, note, busy). The formats are `DownloadMenu`'s (a Part).

- **Parts over it:** `DownloadMenu` holds the formats: the four SPARQL results formats for a SELECT, the six graph formats for a CONSTRUCT or DESCRIBE.

Level: molecule. Cases: `closed`, `open`, `busy`.
