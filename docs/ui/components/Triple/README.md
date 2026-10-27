# Triple

A statement as a row of three `TermChip`s, with a sign column and a note column. The same row serves a resource's description, a commit's delta, a diff between two positions and the explicit-versus-entailed view.

- **Consumer provides:** subject, predicate and object (and the graph, in the note column, when the view spans graphs), plus what the row is: asserted (no modifier), `vw-triple--added` (+), `vw-triple--removed` (−, struck through) or `vw-triple--inferred`.
- **Inferred** rows draw the predicate with a dashed underline in `edge-inferred` and say "inferred" in the note column, naming the rule or the TBox binding when the API reports it. Never a paler row alone: paleness reads as disabled.
- In a diff, rows sort retractions before assertions for the same subject and predicate, so a changed value reads as − then +.
- Below 640px the three terms stack under the sign.
