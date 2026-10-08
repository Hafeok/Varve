# Status fixtures

The failure paths of `eng/status.cs` (ADR 0102, `docs/releases.md` §7). Each
directory with an `expect.txt` is one case, with its own `README.md`,
`roadmap.md` and `passing.txt`; the projects and the release descriptors are
the repository's. Every case must **fail and report every line of
`expect.txt`**, so it fails for its own reason.

```
dotnet run eng/status.cs -- --fixtures tests/fixtures/status
```

The `status-fixtures` job in `eng/ci.cs` runs it.

| Case | The one flaw |
|---|---|
| `stale-status-line` | the opening line v0.1.0-preview.1 shipped with: "Milestone 5c", "Nothing is published yet" |
| `wrong-total` | a conformance total that is not the baseline's line count |
| `package-table` | a packable project with no row, and one with the wrong layer |
| `not-built-exists` | a "not built" row naming the server, which exists |
| `roadmap-headings` | a released milestone not marked *(complete)*, and an unreleased one marked |

Each case is otherwise current against this repository. When a project is
added, or a release, the cases may report it as well; that does not change
whether they say what `expect.txt` asks.
