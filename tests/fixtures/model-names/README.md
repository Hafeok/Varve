# `eng/model-names.cs`'s fixtures

Cases for `eng/model-names.cs --fixtures` (ADR 0241, #169). A `pass-*.txt`
message must be accepted as written; a `fail-*.txt` one must be refused, by the
hook (`--message`) or, for a co-author trailer the hook would rewrite, by the
gate over a range. Each `rewrite-*` directory's `input.txt` must become its
`expected.txt` under `--message`, and the result must pass the gate. The
pipeline's `model-names-fixtures` job runs them on every build.

The versions are fictional (9 and 9.9): the pattern matches any version, and
no real model's identifier is kept in the repository.

| Case | Why |
|---|---|
| `fail-name-alone-in-body` | a family word and a version, in the body |
| `fail-name-with-claude-in-body` | the same after the product name |
| `fail-name-in-subject` | in the subject |
| `fail-name-in-another-trailer` | in a trailer the hook does not rewrite |
| `fail-name-in-co-author-trailer` | in a co-author trailer: the hook rewrites it, the gate refuses it as committed |
| `fail-name-lower-case` | case does not matter |
| `pass-identifier` | the API identifier, hyphenated, is allowed |
| `pass-bare-claude` | the allowed co-author trailer |
| `pass-family-word-without-version` | a family word with no version is not a name |
| `pass-version-without-family` | a version with no family word is not a name |
| `rewrite-co-author-trailer` | the trailer becomes the allowed one |
| `rewrite-folds-a-repeat` | a rewritten trailer and an existing allowed one fold into one |
| `rewrite-leaves-comments` | comment lines are not the message and stay as they are |
