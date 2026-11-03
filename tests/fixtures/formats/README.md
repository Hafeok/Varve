# The format gate's fixtures

Cases for `eng/formats.cs --fixtures` (ADR 0216). Each directory is a small
repository of its own: an inventory in `docs/formats/README.md`, a source file
with a version constant, a fixture under `testdata/formats/` with its
`SHA256SUMS`, and `formats-constants.txt`, the constants the gate watches in
it. The pipeline's `formats-fixtures` job runs them on every build: a `pass-`
case must be judged consistent and a `fail-` case refused.

| Case | Why |
|---|---|
| `pass-consistent` | the constant matches its row, the fixture verifies, the gap is named |
| `fail-a-new-version-without-its-row` | the code is at version 2, the table at 1 |
| `fail-a-row-without-a-fixture` | the row names a fixture that is not there |
| `fail-a-fixture-that-does-not-verify` | the fixture's bytes are not its sums' |
| `fail-a-fixture-no-row-names` | a fixture directory no row names |
| `fail-a-watched-constant-without-a-row` | a watched constant has no row |
| `fail-a-row-with-neither-fixture-nor-gap` | a row with no fixture and no gap |
