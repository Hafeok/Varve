# ResultsTable

A SELECT answer. One column per variable, cells are `TermChip`s, an unbound cell is named "unbound" to screen readers. The caption names the regime that answered, from the echoed `Varve-Entailment`; rows are never marked inferred. The foot is a live status and the actions. The client reads ahead to 5,000 rows and closes the response; the table shows them in steps of 500 and "Show 500 more" never goes back to the server (ruling D2). Parameters: Variables, Rows, Caption, Status, StatusKind, Actions.

- **Read-ahead (ruling D2):** the client reads up to 5,000 rows into the browser, then closes the response; the table shows them in steps of 500, and "Show 500 more" never goes back to the server. An empty answer has its row in the body and no foot.

Level: organism. Cases: `rows`, `regime`, `empty`, `unbound`, `streaming`, `more`, `ceiling`, `cancelled`, `cut`.
