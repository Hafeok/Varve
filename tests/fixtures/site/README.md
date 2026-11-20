# Docs site link gate fixtures

The two paths of the docs site's link gate,
`dotnet run --project eng/site -- check` (ADR 0247). A gate that has never
failed is a gate nobody has tested (`docs/testing.md` §5).

| Case | What it holds | Expected |
|---|---|---|
| `failing` | a link to a file that does not exist, a link to an anchor its file does not make, a working link, and an exemption that matches no link | exit 1: two broken links and one stale exemption |
| `passing` | links to a page and its anchor, to the same page's anchor, to a directory and to another site, and one broken link that an exemption names | exit 0, one exempt |

```
dotnet run --project eng/site -- check --root tests/fixtures/site/failing
dotnet run --project eng/site -- check --root tests/fixtures/site/passing
```

The `site-links-fixtures` job in `eng/ci.cs` requires exactly those exit codes.

`script` is a page whose links are whole and which holds a `<script>`: the
link gate passes it, and `build` refuses it after writing, with exit 1
(ADR 0247 point 6). The `site-script-fixture` job in `eng/ci.cs` runs it.
It also holds the smallest design source a build reads (`docs/ui/tokens.json`,
`bundle.css`, `site/site.css`, `assets/varve-core.svg`) and a
`Directory.Build.targets` stating the licence (ADR 0247 amended 2026-11-19), so
that the one finding, and the reason for the exit code, is the script.

```
dotnet run --project eng/site -- build --root tests/fixtures/site/script --out artifacts/site-script-fixture --commit 0000000000000000000000000000000000000000
```

## What a diagram's SVG may hold

`svg/` proves the check every diagram passes before the build writes it
(ADR 0247 point 3; the maintainer's answer 2 on #129):
`dotnet run --project eng/site -- check-svg <file>…`.

| Case | What it holds | Expected, each file alone |
|---|---|---|
| `svg/failing/script.svg` | a `<script>` | exit 1 |
| `svg/failing/event-handler.svg` | an `onload` attribute | exit 1 |
| `svg/failing/foreign-object.svg` | a `<foreignObject>` holding HTML | exit 1 |
| `svg/failing/other-origin.svg` | an image from another origin | exit 1 |
| `svg/failing/style-import.svg` | a style that imports and references another origin | exit 1 |
| `svg/passing/drawn-by-mermaid.svg` | ADR 0247's own diagram, as Mermaid 12.3.0 drew it | exit 0 |
| `svg/passing/own-fragments.svg` | `url(#…)` and `href="#…"` to its own fragments | exit 0 |

The `site-svg-fixtures` job in `eng/ci.cs` runs each failing file on its own.
