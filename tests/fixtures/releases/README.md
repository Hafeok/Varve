# Release fixtures

The failure paths of `eng/release.cs` (ADR 0102, `docs/releases.md`). A gate
that has never failed is a gate nobody has tested (`docs/testing.md` §5).

Each directory with an `expect.txt` is one case: a `releases/` directory, and
for the changelog case a `CHANGELOG.md`. The case is checked as a pull request
proposing everything in it, against this repository's history and tags, and
must **fail and report every line of `expect.txt`**. That is what makes the
case fail for its own reason and not for another.

```
dotnet run eng/release.cs -- --fixtures tests/fixtures/releases
```

The `release-fixtures` job in `eng/ci.cs` runs it.

| Case | The one flaw |
|---|---|
| `bad-version` | `v0.1.0-preview.02`: a numeric prerelease identifier with a leading zero is not SemVer 2.0.0 |
| `reused-version` | `v0.1.0-preview.1`, which is tagged already |
| `unresolved-basis` | `issue: 999999`, closed by nothing in the range, and `adr: "0999"`, which does not exist |
| `wrong-storage-format` | `storage-format: 99`, where `Varve.Store` writes 1 (ADR 0072) |
| `hand-edited-changelog` | a `CHANGELOG.md` with one line edited by hand, so it is not the projection of its `releases/` |

Each descriptor is otherwise a real one. Findings that come from the
repository moving on, such as an ADR list that no longer matches once the next
release is tagged, may join a case's output. They do not change whether the
case says what `expect.txt` asks.

`dry-run/` is not a case. The release workflow plans it in a dry run when
`releases/` has nothing pending, so that the path runs before the first real
release. It names the next version and goes stale once that version is cut;
the close-out that cuts it updates or removes it.
