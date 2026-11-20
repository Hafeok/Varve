# Triple

A statement as a table row (`<tr>`, in a `TripleList`) of three `TermChip`s, with a sign column and a note column. The sign is shown as `+` or `−` and read out as its word, "added" or "removed". The same row serves a resource's description, a commit's delta, a diff between two positions and the explicit-versus-entailed view.

- **Consumer provides:** subject, predicate and object (and the graph, in the note column, when the view spans graphs), plus what the row is: asserted (no modifier), `vw-triple--added` (+), `vw-triple--removed` (−, struck through) or `vw-triple--inferred`.
- **Not asserted in this graph** rows (`vw-triple--inferred`) draw the predicate with a dashed underline in `edge-inferred`, and the word in the note column tells the two kinds apart (#96, ruling D4):
  - **"inferred"**: what the regime added, the regime's answer less `simple`'s at the same position;
  - **"from the ontology"**: of what the regime added, a triple the readable TBox graphs assert, which the regime makes visible in this graph.

  The note never names a rule: the rule is shown only from `explain`, by a "Why?" control that waits on #171 (#349). Never a paler row alone: paleness reads as disabled.
- In a diff, rows sort retractions before assertions for the same subject and predicate, so a changed value reads as − then +.
- Below 640px the three terms stack under the sign.
