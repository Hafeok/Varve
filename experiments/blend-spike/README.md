# Blend spike (#128)

Can a layered library's allowed decisions be *generated* from a few spaces
and blend rules, instead of being listed one by one? The evidence is ten of
Varve's accepted ADRs. The verdict and the table are in
[`claude/varve-blend-spike-findings.md`](../../claude/varve-blend-spike-findings.md).

This directory is never merged and ships nothing. It is outside `Varve.slnx`
and outside the DD and VARVE rules **by layout**: `Directory.Build.props`
here does not import the repository's (the maintainer approved this as
layout, not suppression). `negative-control/` imports the repository's
settings whole, because its point is to be checked by DD0001 and DD0002.

| Path | What |
|---|---|
| `vocab/blend.ttl` | the `blend:` vocabulary, twenty terms |
| `vocab/lib.ttl` | the layered-library domain terms constraints are written in |
| `ontology/spaces.trig` | accepted rulings annotated as spaces, each encoding beside its statement; the spike's proposed spaces |
| `ontology/rules.trig`, `rules/*.rq` | the rules; R1 is ADR 0003's `LayersStrictlyDownward`, R2 to R6 are proposed |
| `attempts/attempts.trig` | every derivation attempt, failed ones included, with the prior guess |
| `check/*.rq` | the three criteria, `emergent` (computed only), inherited acceptance, the reports |
| `shapes/*.ttl` | SHACL: layer direction, `InternalsVisibleTo`, the level invariant, live contradictions, `emergent` only computed |
| `BlendSpike/` | the host: the projector, an embedded file-backed Varve dataset, the rules on Varve's evaluator, the as-of question |
| `negative-control/` | an upward and a same-layer reference, and an `InternalsVisibleTo` grant as an item and in source |
| `out/` | everything the run wrote, and the analyzers' own output |
| `docs/traceability/` | the session record (ADR 0033) |

## Running it

```bash
dotnet build experiments/blend-spike/BlendSpike -c Release
dotnet run --project experiments/blend-spike/BlendSpike -c Release --no-build -- . experiments/blend-spike <new dataset directory>
python -m venv <venv> && <venv>/bin/pip install pyshacl          # pyshacl 0.40.1, rdflib 7.6.0 in the run
<venv>/bin/python -I experiments/blend-spike/check/shacl.py experiments/blend-spike
grep -o 'Path="[^"]*"' Varve.slnx | sed 's/Path="//; s/"$//' > <solution list>
<venv>/bin/python -I experiments/blend-spike/check/compare_layers.py experiments/blend-spike <solution list>
```

The host needs the full git history (`git fetch --depth=5000 origin main` in
a shallow clone). One query against a dataset a run wrote:
`BlendSpike query <dataset directory> rules/_prefixes.rq <query file>`.
