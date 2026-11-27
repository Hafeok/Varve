# GraphGroup

A delta or a diff grouped by graph, in graph IRI order with the default graph first. It takes each group's heading, count and body as slots (the page passes a `TripleList`), so it uses no organism. The summary counts what is returned to the caller and never claims a graph is hidden (ruling 2). Parameters: Summary, Groups (title, count, body).

Level: organism. Cases: `delta`, `one-graph`, `empty`.
