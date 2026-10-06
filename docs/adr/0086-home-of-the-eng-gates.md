# 0086 — Home of the eng/ gates

## Status

**Accepted — decided by the maintainer, 2026-10-06, and written by the
pre-release session of #63.** The decision set is filed without
`accepted-by`, which a session never writes (ADR 0066); the maintainer adds
it.

Complements [0036](0036-containerised-development.md) (every gate is a job in
`eng/ci.cs`) and [0039](0039-repo-standard.md) (repo-standard moves out);
supersedes nothing.

## Context

`eng/` holds the repository's gates as scripts. Some exist because of what Varve is, such as the
conformance ratchet. Others hold no knowledge of Varve. They make the
stewardship standard or the decision ledger executable, and every project
that adopts either would have to write them again. Until now nothing said
which kind is which, or where the generic ones should end up.

## Decision

**`eng/` is the home of Varve-specific gates**:
- the conformance ratchet and its exemptions (`ratchet.cs`, `baseline/`);
- the guard counts and harnesses (`durability.cs`, `browser-tests.cs`);
- the benchmarks;
- the decision report against Varve's baseline (`decision-report.cs`);
- `ci.cs` as the orchestrator of every gate, wherever the gate's code lives.

**It hosts the generic gates only temporarily, until they are ported.** There
are two destinations.

**Process gates go to `mindovermachine-dev/how-we-work`**, beside
repo-standard, as the stewardship standard made executable:

| Gate | Today |
|---|---|
| Changelog, with the release cut | `eng/changelog.cs`, `eng/changelog-sections.txt` |
| Licence headers | `eng/licence-headers.cs` |
| Issue references | `eng/issue-refs.cs` |
| DCO sign-off | `eng/dco.cs`, with the identity map it reads (`eng/identities.json`, `eng/lib/Identities.cs`; ADR 0087) |
| Package metadata | `eng/package-metadata.cs` |
| Native assets | `eng/native-assets.cs` |
| Release pending (ADR 0085) | `eng/release-pending.cs` |

**Ledger gates go to the analyzer repository**, beside `DecisionDriven.Report`:

| Gate | Today |
|---|---|
| Register citations | `eng/dependency-register.cs`; `eng/banned-symbols.cs`, which enforces ADR citations on banned-symbol entries |
| Decision-set checks the generator does not make | `eng/decision-sets.cs`, apart from duplicate, non-identifier and colliding keys, which `DDGEN0001`, `0002` and `0005` already report. Varve's own rules (the `varve` namespace, `adr:` naming a `docs/adr/` file) remain configuration or a local check |

Suppression citations are not in this list. They are `DD0008`, which already
lives in the analyzer package. The ported register gates cite **decision
keys** rather than ADR numbers.

### Exit criteria

1. **One issue in each destination**, and the provenance recorded on Varve's
   side:
   - process gates: [mindovermachine-dev/how-we-work#1](https://github.com/mindovermachine-dev/how-we-work/issues/1),
     listing the scripts and the Varve commit each comes from;
   - ledger gates: [Hafeok/decision-driven-analyzers#84](https://github.com/Hafeok/decision-driven-analyzers/issues/84).
     That repository never names a consumer, so the issue describes the gates
     generically; the scripts, their Varve commits and the link to it are in
     [#65](https://github.com/Hafeok/Varve/issues/65).
2. **A script leaves `eng/` when its port is released and Varve consumes it
   from there.** Until then, the script in `eng/` is the gate, and `eng/ci.cs`
   runs whichever form is current.

## Alternatives considered

- **Keep everything in Varve.** Rejected: every adopter of the standard or
  the ledger would re-implement the same gates, and each copy would drift.
- **Port now.** Rejected: porting blocks the release (ADR 0085), and a port
  is not done until the destination publishes the gate and Varve consumes it.
  The gates work where they are.

## Consequences

- A new generic gate may still be written in `eng/`, but it joins one of
  the two issues when it is written.
- Fixes to a generic script in the meantime are carried into its port. The
  Varve commit listed in the issue is updated when the script changes.

## Checks

- **Checked against the accepted ADRs** (0001–0085). Consistent with **0036**
  (`eng/ci.cs` stays the one entry point), **0039** (the same direction for
  repo-standard), **0062** and **0063** (the ledger and the report belong to
  the analyzer repository), **0009** (the register's rules, which travel with
  it). No conflict.
- **Layer ownership.** None; build and process.
- **Analyzer rule.** None.
- **Open questions owned.** None.
