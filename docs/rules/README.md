# Analyzer rules

One page per rule. Each page states what the rule reports, why it exists, how
to satisfy it, and when suppressing it is legitimate. Every page links to the
ADR that motivates the rule, and every analyzer's `HelpLinkUri` links here.

Ids are allocated in [ADR 0004](../adr/0004-enforcement-by-analyzers.md) and
are never reused.

| Id | Rule | State |
|---|---|---|
| [VARVE0001](VARVE0001.md) | Layer direction | Implemented |
| [VARVE0002](VARVE0002.md) | Layer declaration | Implemented |
| VARVE0003 | `InternalsVisibleTo` only toward `*.Tests` | Reserved |
| VARVE0004 | No `Common`, `Core`, `Utils`, `Helpers` or `Abstractions` | Reserved |
| VARVE0005 | No mutable static state or static registries | Reserved |
| VARVE0006 | Hot path discipline | Reserved |
| VARVE0007 | Public contracts use `Varve.Rdf` types or BCL primitives | Reserved |
| VARVE0008 | A suppression must cite an ADR | Reserved |

A reserved id is allocated and agreed in principle. It is not a rule until the
analyzer exists, and nothing in this repository relies on one.
