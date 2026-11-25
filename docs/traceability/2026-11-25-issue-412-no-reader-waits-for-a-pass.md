# No reader waits for a pass (#412)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim
> below. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#412](https://github.com/Hafeok/Varve/issues/412), completed; a 1.0 item of [#15](https://github.com/Hafeok/Varve/issues/15). Split from [#408](https://github.com/Hafeok/Varve/issues/408) |
| **Date** | 2026-11-25 |
| **Tool** | Claude Code 2.1.332, a cloud session. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* This tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYRX4JikPScFa2DSHxt` |
| **Branch** | `claude/readiness-snapshot-412`, from `main` at 36e77d45 |
| **ADRs** | [0195](../adr/0195-the-inference-projection.md) amended 2026-11-25, unaccepted |

## The prompts

The session is the one that fixed #399. On 2026-11-25 the maintainer
forwarded the architect's message for it, which ended:

> One addition, as an issue of its own, not in #409: `GET /health/ready`,
> explain and shutdown block on a closure pass's lock. Split that out of
> #408. Say how long each waits during an ordinary build at REC M. Propose a
> fix. Link it from #15 as a 1.0 item.

The session filed #412 with its measurements and a proposal. The
maintainer then wrote, on 2026-11-25:

> B. #412 is next, ahead of everything else, as a 1.0 blocker. Your
> measurements and proposal are accepted. Rulings:
> 1. A closure build or pass never makes the instance or a dataset unready.
>    Reads go on, and an answer without the closure says so in-band as it
>    does today.
> 2. Readiness answers from a snapshot published after each pass, never
>    under the pass's lock, in constant time. It names a pass in progress:
>    kind, position, how long it has run. No probe computes clashes.
> 3. A shutdown cancels a build. Say what is saved, so the next start does
>    not build cold, and the bound after which a pass is abandoned.
> 4. Explain never waits behind a probe; say what it answers during a pass.
> 5. Tests: probes during a cold build and while idle, each answering within
>    a stated bound at REC M; a shutdown during a build that ends within the
>    grace period. Correct `docs/operator/inference.md`. File rulings
>    unaccepted as a dated amendment to the ADR that owns readiness for
>    inference. Post the plan on #412 before building.

The plan was posted on #412 before any product code changed.

## The report

**What waited, before** (REC M, OWL 2 RL, cross-graph, 4 cores): a cold
build held the projection's lock for 290 s; every readiness probe during it
waited past the probes' 1 s timeout, the longest 450 s; idle, readiness's
clash count took 7.2 s; a stop's cancellation was never read, and the build
returned 290 s after it.

**The change.**
- A closure keeps its clashes per pass (`KeptClashes`): every check matched
  once after a build, then kept from each pass's changed facts. A property
  test holds it to every check matched in full at every commit.
- The projection publishes an `InferenceSnapshot` at the end of each pass,
  and an `InferencePass` while one runs. Readiness, `Varve-Inconsistent` and
  `Clashes()` read the snapshot without a lock; readiness writes `pass`.
- `Closure.Apply` takes a cancellation token, seen within a thousand rule
  firings; the feed's stop token reaches every pass. A cancelled pass drops
  its closures, keeps the position and saves nothing. The server's stop
  waits at most `Varve:Inference:StopWait`, 10 s, and saves after a cold
  build, every 1,000 commits or 5 minutes, and at a stop that cut no pass.
- Explain at the projection's position waits at most two seconds for the
  pass, then answers `inference-behind` naming it.

**What a start does with what is saved.** It loads the state and builds the
closures at its position to check them, as ADR 0195 point 5 requires: the
history and the position survive a restart, the build's time does not. The
amendment leaves restoring without a build as an open question for the
maintainer.

**Measured after**, REC M: @@RECM-SUMMARY@@

**Tests.** `ClashesKeptPerPassTests` (the kept clashes equal a full match,
and a mutant that keeps stale clashes fails it); `PassTests` (probes during a
cold build, explain during a pass, a cancelled build); and
`InferencePassServerTests` (readiness over HTTP in a child process during a
cold build and idle, and a SIGTERM during a cold build ending the process
within 30 s, then a restart that loads). The nightly's `pass waits` job holds
REC M to the bounds.
