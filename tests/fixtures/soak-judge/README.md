# The soak judge's fixtures

Soak outputs written by hand, one for each thing the judge (ADR 0215,
`tests/Varve.Benchmarks/SoakJudge.cs`) must say. The pipeline's
`soak-judge-fixtures` job runs `--soak-judge-fixtures` on this directory on
every build, so the gate's failure path is proven, not assumed. A fixture's
name is its expected verdict:

| Prefix | Exit | Fixtures |
|---|---:|---|
| `holds-` | 0 | `flat`: the working set less the dataset's own within a few MB of its median, the dataset steady from minute 25 |
| `misses-` | 1 | `drift`: minutes 50–60 about 13% over 30–40; `band`: one sample at 200 MB against a median of 130; `handles`: handles climbing past the bound |
| `cannot-` | 2 | `growing`: the dataset grows for the whole run; `short`: forty minutes, less than the steady state and twenty minutes more |

Each is a sample every half minute in the soak's own columns
(`Soak.Header`); only `minutes`, `quads`, `working set MB`, `own MB`,
`open handles` and `derived files` vary.
