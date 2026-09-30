# VARVE0002 — Retired: layer declaration

| | |
|---|---|
| **State** | **Retired** 2026-09-26, by [ADR 0062](../adr/0062-adopting-decisiondriven-analyzers.md) |
| **Replaced by** | [`DD0001`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0001.md) for a referenced family assembly that declares no layer; [`VARVE0005`](VARVE0005.md) for the rest |
| **Motivated by** | [ADR 0003 — Package layering](../adr/0003-package-layering.md), [ADR 0060](../adr/0060-hosts-at-layer-6-the-composition-root.md) |

This id reported every way a `VarveLayer` declaration could be missing,
malformed or worked around. It split in two when the layer moved to the
package's `ArchLayer` (ADR 0064):

- **A reference to an undeclared assembly** is `DD0001`'s "declares no layer".
- **An assembly that declares nothing itself**, a packable assembly with no
  layer, an executable below layer 6, a library at layer 6, and the new
  agreement between `ArchLayer` 6 and `ArchCompositionRoot`, are
  [`VARVE0005`](VARVE0005.md), which ADR 0064 allocates.

Two checks were not carried over, because nothing can now fail them: a
referenced assembly with malformed `Varve.Layer` metadata (the package's
generated `[ArchLayer]` takes an integer), and `Varve.Analyzers` referenced as a
library (the analyzer's own test project is the only compilation that
references it, and the fixtures build every other project against it as an
analyzer).

The id is **retired, not released**: it reported, so it is never reused. This
page stays so that a link or an old build log naming `VARVE0002` still resolves
to what happened.
