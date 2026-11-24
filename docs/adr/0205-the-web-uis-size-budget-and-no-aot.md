# 0205 — The Web UI's size budget, and why it is not AOT-compiled

## Status

**Accepted — filed unaccepted by Web UI part 1 (#167, milestone #96),
2026-10-28** (ADR 0066). Acceptance is the maintainer's act on the pull
request. The rule for AOT was stated on the part 1 plan (#96, 2026-10-27)
before the numbers existed; this ADR records the numbers and what the rule
chose.

Records decision set [`the-web-uis-size-budget-and-no-aot`](../decisions/the-web-uis-size-budget-and-no-aot.md).

## Context

The Web UI (ADR 0200) is downloaded before an operator sees anything.
Blazor WebAssembly can run interpreted, or with the app's assemblies compiled
ahead of time to WebAssembly (`RunAOTCompilation`). AOT runs code faster and
downloads more. A console is I/O-bound user interface: it waits on the server
far more than on its own code.

A size that is only measured drifts. ADR 0200 measured the first screen at
1.675 MB. Each slice since has added to it, and nothing stops the next from
adding more without anyone deciding to.

## Decision

1. **The rule (from the plan):** AOT only if the time it saves on the first
   screen exceeds the time its extra bytes cost at 10 Mbit/s, which is about
   0.8 s per MB.
2. **The numbers.** Measured on 2026-10-28 at this milestone's head:
   - each build published from a clean tree;
   - the Health screen served by `Varve.Server` (ADR 0202), loaded in
     Chromium;
   - navigation to the management panel rendered, cold cache, median of 10.

   | Build | Download (brotli-11 of `_framework/`) | First screen, median |
   |---|---|---|
   | Interpreted (trimmed, relinked) | **1.823 MB** | **1077 ms** |
   | AOT (`RunAOTCompilation`) | 4.108 MB | 1128 ms |

   AOT saves no time: it is 51 ms slower on loopback, where its 2.29 MB more
   cost nothing to fetch. At 10 Mbit/s those bytes cost about 1.8 s more.
   **The Web UI is not AOT-compiled.**
3. **The budget gate.** `eng/browser-size.cs` sums `_framework/` compressed
   with brotli at quality 11 and window 24, skipping `.br`, `.gz`, `.map` and
   `.pdb`, and compares the sum with `eng/browser-size.json`.
   - **The budget is the measured size plus 5%, rounded up to 10 kB:**
     1,822.8 kB measured, 1,920,000 bytes budgeted.
   - **Over budget fails** (exit 1). **Raising the budget is an amendment to
     this ADR**, so a growth is a decision someone made.
   - **More than 5% under** prints the line to lower it, as the conformance
     ratchet prints its new passes. It does not fail.
   - It runs in the `browser wasm` job, on the publish that job already makes
     from a clean tree. That job runs the trimmed publish with warnings as
     errors, so a trim warning fails before the size is measured.
4. **Measure only from a clean tree.** A publish into a folder that held an
   earlier one keeps that one's fingerprinted files, which measured 5.2 MB
   here. A publish whose `obj/` an AOT publish wrote reuses its IL-stripped
   assemblies: same raw size, different bytes, 1.55 MB here. CI's checkout is
   clean; a local number is from `rm -rf obj bin` and an empty output folder.

## Alternatives considered

- **AOT.** Refused by the rule, on the numbers in point 2.
- **Partial AOT** (`WasmProfilers` and profile-guided AOT of hot methods).
  There are no hot methods to profile: the first screen is network and
  rendering.
- **A budget on the whole `wwwroot`.** The stylesheets, the core mark and
  `index.html` are a few kB, and they change with the design, not the code.
  `_framework/` is what grows.
- **The WebAssembly smoke test's store variant under the same gate** (the
  plan's second budget). WasmSmoke is a test and ships nothing, and CI builds
  it without trimming. Its size is the store's, not an operator's download.
  Left out; it can join with its own row in `eng/browser-size.json`.
- **A job of its own, `browser size`** (the plan). It would publish the Web
  UI a second time. A step in `browser wasm`, which already publishes it, does
  the same in one publish.

## Consequences

- `browser wasm` fails when the Web UI outgrows its budget.
- Part 2 (the data explorer) is the same application. Its growth is an
  amendment here, with its numbers.
- **Checks:**
  - Against the accepted ADRs: 0200 (the host, trimming, relinking), 0202
    (served by the server, as measured), 0036 (no browser in the
    devcontainer: the gate needs none). No conflict.
  - Layer ownership: `eng/`, outside the layers.
  - Analyzer rule: none. The gate is a script, and its failure path was run
    (a 1.8 MB budget failed; a 2.1 MB one printed the ratchet line).

## Amendment, 2026-11-19 — two rows: the first screen, and the explorer's lazy set

Filed by the session for Web UI part 2 (#355, under #96), unaccepted (ADR 0066), and accepted by the maintainer on 2026-11-24. Decided by the maintainer's ruling 5 of
2026-11-02 and D1 of 2026-11-18: the explorer's assemblies are lazy-loaded,
the console's first screen keeps this ADR's budget, and the explorer gets its
own stated budget, with both numbers measured.

1. **Two rows.** `eng/browser-size.cs` reads the boot manifest's
   `lazyAssembly` entries (in `dotnet.js` in .NET 10) and measures:
   - **`webui`, the first screen:** every file under `_framework/` but the
     lazy set. Its budget stays 1,920,000 bytes.
   - **`webui-explorer`, the lazy set:** the assemblies the manifest marks
     lazy. Its budget is set by point 3's own rule.
   The ruling is `TwoRowsFirstScreenAndLazySet`.
2. **The lazy set is named.** `webui-explorer.assemblies` lists the lazy
   assemblies, and the gate fails when the manifest's set differs: one lazy
   that the file does not name, or named and loaded with the first screen.
   Moving an assembly between the rows is an amendment here. The ruling is
   `TheLazySetIsNamed`.
3. **The numbers**, measured on 2026-11-19 from a clean tree, interpreted
   and trimmed, after ADR 0252's split (revised in place before acceptance:
   the first measurement, before the split, is kept for the record):

   | Row | Before the split | After the split | Budget |
   |---|---|---|---|
   | `webui`, the first screen | 1,901.6 kB | **1,884.6 kB** | 1,920.0 kB (unchanged) |
   | `webui-explorer` | 45.4 kB (`Varve.Sparql`) | **48.4 kB** (`Varve.Sparql` 45.6, `Varve.WebUi.Explorer` 2.8) | **60.0 kB** |

   The first screen drops by 17.0 kB because the explorer's components and
   parts leave it. The explorer's budget is its measurement plus 5%, rounded
   up to 10 kB. The trimmer keeps the explorer's pages and parts by a
   descriptor (`ExplorerRoots.xml`): the host names no type of it, so it
   would otherwise drop it whole, and rooting it whole would keep the
   decision ledger every assembly carries as generated types (76.4 kB).
   `Varve.Sparql.Results` joins the lazy set when a page streams a query; it
   is trimmed out until then. The ruling is `ExplorerBudgetMeasured`.

4. **With the Query screen** (measured 2026-11-19, the same way), the
   first screen holds the explorer's reads in `Varve.Protocol.Client`
   (13.3 kB more, now reachable) and the components the Query screen
   composes. `Varve.Sparql.Results`, the client's TSV reader, which only
   the Query screen's streamed answers reach, joins the lazy set, as point
   3 said it would:

   | Row | After the split | With the Query screen | Budget |
   |---|---|---|---|
   | `webui`, the first screen | 1,884.6 kB | **1,907.7 kB** | 1,920.0 kB (unchanged) |
   | `webui-explorer` | 48.4 kB (48,357 B) | **91.1 kB** (91,087 B: `Varve.Sparql` 45,439, `Varve.WebUi.Explorer` 26,292, `Varve.Sparql.Results` 19,356) | **100.0 kB** |

   The explorer's 60 kB was a placeholder, set from a lazy set with no page
   in it, not a measurement. The maintainer's ruling of 2026-11-20 on #355
   replaces it under point 3's rule, which does not weaken the gate:
   91,087 B plus 5% is 95,641 B, rounded up to 10 kB, 100,000 B. The
   ruling `ExplorerBudgetMeasured` states these numbers; its first statement,
   60,000 bytes for two assemblies, was replaced before acceptance.

5. **With the explorer-only components moved** (ADR 0252 point 7,
   2026-11-20): the components only explorer pages use, and those designed
   for the explorer's screens, leave the first screen for the lazy set.

   | Row | With the Query screen | With the components moved | Budget |
   |---|---|---|---|
   | `webui`, the first screen | 1,907.7 kB | **1,902.9 kB** | 1,920.0 kB (unchanged) |
   | `webui-explorer` | 91.1 kB | **96.9 kB** (96,883 B: `Varve.Sparql` 45,407, `Varve.WebUi.Explorer` 32,009, `Varve.Sparql.Results` 19,467) | 100.0 kB (unchanged) |

   `Varve.WebUi.Components` drops 4.5 kB (30,871 B to 26,360 B) and the
   explorer gains 5.7 kB (26,292 B to 32,009 B): the Query screen's
   components (`QueryEditor`, `ResultsTable`, `AskAnswer`, `Menu`, `Select`
   and `TermValue`) change rows. The graph's and validation's components are
   on no page, so the trimmer drops them from either row. The first screen
   drops 4.8 kB, and is 17.1 kB under its budget.

6. **The explorer's parts kept through its pages** (2026-11-24, the size
   review on #355). The descriptor `ExplorerRoots.xml` now keeps only the
   explorer's pages, which the router finds by reflection. A part or a
   component is kept through the pages that use it, so one no page uses
   stays out of the lazy set. The descriptor of point 3 kept the pages and
   the parts whole. `RegimeComparison`, which no page uses yet, left the
   lazy set. Measured from a clean publish at the accepted rulings:

   | Row | Parts rooted whole | Parts kept through the pages | Budget |
   |---|---|---|---|
   | `webui`, the first screen | 1,905,119 B | **1,905,076 B** | 1,920.0 kB (unchanged) |
   | `webui-explorer` | 98,783 B | **96,385 B** (`Varve.Sparql` 45,380, `Varve.WebUi.Explorer` 31,665, `Varve.Sparql.Results` 19,340) | 100.0 kB (unchanged) |

   The first screen's 43 bytes are the boot manifest's file names.

Both failure paths were run, before the split: the set naming
`Varve.Sparql.Results`, which is not lazy, fails with the difference; a
40,000-byte budget fails 5.4 kB over. After the split, the first measurement's
50,000-byte budget failed 72.0 kB over with the explorer rooted whole, which
is how the ledger was found.
