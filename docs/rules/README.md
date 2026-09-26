# Analyzer rules

Varve's own rules, under the `VARVE` prefix, from `Varve.Analyzers`. One page
per rule: what it reports, why, how to satisfy it, and its exception path.
Every page cites the ADR that motivates the rule, and every analyzer's
`HelpLinkUri` links here.

The generic rules, `DD0001` to `DD0019`, come from
[`DecisionDriven.Analyzers`](https://github.com/Hafeok/decision-driven-analyzers)
and are documented in that repository's
[`docs/rules/`](https://github.com/Hafeok/decision-driven-analyzers/tree/main/docs/rules)
(ADR [0062](../adr/0062-adopting-decisiondriven-analyzers.md)). Varve configures
them and never changes them (ADR [0064](../adr/0064-varve-configuration-and-hot-path-rules.md)).

Ids are allocated by ADR 0064 and are never reused. ADR 0004's reservation
table is retired by ADR 0062.

| Id | Rule | State |
|---|---|---|
| [VARVE0001](VARVE0001.md) | Layer direction | **Retired**, replaced by `DD0001` |
| [VARVE0002](VARVE0002.md) | Layer declaration | **Retired**, replaced by `DD0001` and `VARVE0005` |
| [VARVE0003](VARVE0003.md) | A hot path does not allocate, and calls only hot-path code | Implemented |
| [VARVE0004](VARVE0004.md) | A hot path's signature does not force allocation or dispatch | Implemented |
| [VARVE0005](VARVE0005.md) | Layer declaration is missing, malformed, or disagrees with the assembly | Implemented |

No `VARVE` rule is suppressed. `DD0008` reports a `#pragma`, a
`[SuppressMessage]` or an `.editorconfig` downgrade for any `VARVE` id, because
`.editorconfig` sets `dd_rule_id_prefixes = VARVE`. The only exception path is
`[DesignDecision(typeof(<Set>.<Key>), Scope = …)]` on the symbol, citing a filed
decision.
