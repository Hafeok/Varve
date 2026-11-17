# The API freeze gate's fixtures

`eng/api-freeze.cs --fixtures tests/fixtures/api-freeze` runs every case in
[`cases.txt`](cases.txt), and `eng/ci.cs` runs it as `api-freeze-fixtures`
([ADR 0227](../../../docs/adr/0227-the-public-api-freeze-gate.md)).

**The package** is `Varve.Fixture.ApiFreeze`, in three surfaces, each a
project of its own with its own `PublicAPI` files, so that `RS0016` and
`RS0017` hold for each:

| Surface | `Gauge` |
|---|---|
| `v1/` | `Read()` and `Reset()`: the baseline |
| `removed/` | `Read()` only: `Reset()` is removed, which is `CP0002` |
| `added/` | `Read()`, `Reset()` and `Peek()`: grown, not broken |

**The run** packs `v1/` into a temporary directory, then packs each other
surface against it through `PackageValidationBaselinePath`, so nothing is
fetched from nuget.org. Every build output goes to the temporary directory
too. None of these projects is in `Varve.slnx`, and none is published.

**A case** names a surface, the baseline's version (which decides whether
the 1.0 rule applies, or `none` for a package never published), a file of
commits since the baseline, a proposed descriptor, and the result it must
give: `pass`, `fail` or `new`.

- `commits/*.txt`: records separated by a line `---`, each beginning
  `touches: <path>…`, then the commit message. The fixture package stands
  for `src/Varve.Fixture.ApiFreeze/`.
- `descriptors/*.yaml`: a proposed descriptor; only its `version` and
  `summary` are read.

What the cases prove:
- before 1.0, an unmarked break fails;
- a `!` subject, a `BREAKING CHANGE:` footer, or a descriptor item naming the
  package with `(breaking)` makes it pass;
- a `!` commit on another package, or a `(breaking)` item about another
  package, does not;
- from 1.0, a break fails even when marked, unless the proposed release
  moves the major.
