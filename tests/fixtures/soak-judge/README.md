# The soak judge's fixtures

Soak outputs written by hand, one for each thing the judge (ADR 0215,
`tests/Varve.Benchmarks/SoakJudge.cs`) must say. The pipeline's
`soak-judge-fixtures` job runs `--soak-judge-fixtures` on this directory on
every build, so the gate's failure path is proven, not assumed. A fixture's
name is its expected verdict:

| Prefix | Exit | Fixtures |
|---|---:|---|
| `holds-` | 0 | `flat`: the working set less the dataset's own within a few MB of its median, the dataset steady from minute 25; `collector-headroom`: every fifth sample the collector commits 40 MB it does not use, and the working set rises with it; `shrinking`: the working set falls by a sixth from minute 30 to the end |
| `misses-` | 1 | `drift`: minutes 50–60 about 18% over 30–40; `band`: one sample far over the median; `committed`: the collector's committed memory climbs past twice the live heap and 128 MB; `handles`: handles climbing past their bound; `derived`: `derived/` files climbing past theirs |
| `cannot-` | 2 | `growing`: the dataset grows for the whole run; `short`: forty minutes, less than the steady state and twenty minutes more; `collector`: the server collector, not the shipped one |

Each is a sample every half minute in the soak's own columns
(`Soak.Header`), after the lines the soak prints before its table: the
collector it ran under and its handles before the dataset opened. Only
`minutes`, `quads`, `working set MB`, `own MB`, `committed MB`,
`open handles` and `derived files` vary; the bounds are the ones
`SoakBounds` gives the fixtures' commits and quads.
