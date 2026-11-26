# ValidationReport

One binding's report, read from its report graph at one position. Grouped by shape or by focus node (the `Tabs` molecule). Severity is a `StateBadge` with its word (violation critical, warning warn, info neutral; the mapping is `Tones`', ADR 0206 point 5). A path is written in SPARQL path syntax. A `503 validation-behind` and the withheld report are the page's: the first as `ProblemPanel`, the second as the `not-returned` case. Parameters: Conforms, Count, Position, Grouping, Groups (title, count, rows).

Level: organism. Cases: `conforms`, `by-shape`, `by-focus`, `severities`, `not-returned`.
