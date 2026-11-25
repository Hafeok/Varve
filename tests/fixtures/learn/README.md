# The learning track's gate: fixtures

The failure paths of `eng/learn.cs` (ADR 0249). Each directory is a small
repository: `docs/learn/` with one page and `terms.md`, one sample under
`samples/learn/`, the one `src/` project it references, and a stub
`eng/ci.cs` naming the job `learn`. A case with an `expect.txt` must **fail
and report every line of it**, so it fails for its own reason. `passing`
must hold.

```
dotnet run eng/learn.cs -- --fixtures tests/fixtures/learn
```

The `learn-fixtures` job in `eng/ci.cs` runs it.

| Case | The one flaw |
|---|---|
| `passing` | none |
| `unmarked-block` | a fenced block with no marker before it |
| `stale-output` | an expected file that differs from the block quoting it |
| `missing-region` | a source marker naming a region the file doesn't have |
| `unknown-job` | a marker naming a job eng/ci.cs doesn't have |
| `none-without-section` | a `none` on a page with no "Not run in CI" section |
| `example-without-words` | an `example` that says nothing |
| `term-too-early` | a chapter 2 term on chapter 1's page |
| `term-without-row` | a term in no row of `terms.md` |
| `term-as-iri-in-data` | a chapter 2 term, as a full IRI, in chapter 1's data |
| `shipped-marker` | an unshipped marker for an issue a released descriptor's basis holds |
| `marker-without-quote` | an unshipped marker with no "Not in this release" quote after it |
| `project-reference` | a sample with a ProjectReference |
| `not-packable` | a VarveReference to a project that is not packable |
| `internals-visible` | a project under `src/` admitting the sample by InternalsVisibleTo |
