# Schema golden files

The JSON Schema generator's output is under the read-forever rule (ADR 0145):
a schema served at its `$id` never changes content, so the generator may never
change what it writes for an existing declaration.

Each case holds a catalogue's history, `history/NN.trig`, one state per
commit, and `expected/{type}/v{version}/r{revision}.json`, the bytes of every
revision of every version of every type, with `$id` under the base
`https://varve.example.org/` and the dataset `golden`. `GoldenSchemaTests`
replays the history and compares every byte. A difference fails the test.

The files were written once, by running the tests with
`VARVE_WRITE_GOLDEN=1` on a working copy with no `expected/` folder; the
switch writes only missing files and is refused when `CI` is set. They are
never regenerated: a generator change that alters one is a defect, or a
change of the read-forever rule, which an ADR decides.

A case that crosses the move of a dataset's declarations to the management
plane (ADR 0167) adds `history/mNN.trig`: the catalogue, in the catalogue
graphs' form, at management position NN after the move. The test continues
the dataset's history from its last state (`EventCatalogueHistory.ContinueFrom`)
and replays them; a revision minted there is written `rm{NN}.json`. The case
`moved` is the first.
