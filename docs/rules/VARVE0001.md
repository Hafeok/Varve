# VARVE0001 — Retired: layer direction

| | |
|---|---|
| **State** | **Retired** 2026-09-26, by [ADR 0062](../adr/0062-adopting-decisiondriven-analyzers.md) |
| **Replaced by** | [`DD0001`](https://github.com/Hafeok/decision-driven-analyzers/blob/main/docs/rules/DD0001.md), *a reference within a family points strictly downward*, from `DecisionDriven.Analyzers` |
| **Motivated by** | [ADR 0003 — Package layering](../adr/0003-package-layering.md), with ADR [0060](../adr/0060-hosts-at-layer-6-the-composition-root.md)'s table |

This id reported a reference from a `Varve.*` assembly to one whose declared
layer was not strictly lower. `DD0001` enforces the same rule under the
package's `ArchLayer` and `ArchFamily=Varve`, reading the referenced layer from
the `[assembly: ArchLayer(n)]` the package generates.

The analyzer, its tests and the `VarveLayer` property it read were removed in
session 2 of #43. The id is **retired, not released**: it reported, so it is
never reused (ADR 0004's id scheme). This page stays so that a link or an old
build log naming `VARVE0001` still resolves to what happened.

A reference that is genuinely intended is not suppressed:
`[DesignDecision(typeof(<Set>.<Key>), Scope = …)]` on the referencing type,
citing a filed decision, is the only exception path (ADR 0062).
