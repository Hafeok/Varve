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
