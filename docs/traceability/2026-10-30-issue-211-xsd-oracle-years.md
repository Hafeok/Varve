# The xsd oracle draws the years whose every timezoned instant the runtime holds

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#211](https://github.com/Hafeok/Varve/issues/211) |
| **Date** | 2026-10-30 |
| **Tool** | Claude Code 2.1.314 (Claude Code), a local session in WSL |
| **Model** | `claude-fable-5-1`, from the session's own metadata |
| **Session identifier** | `99e6699f-a3f7-48ac-974c-711ade42f85c` |
| **Branch** | `fix/xsd-oracle-years`, from `main` at 826d67b9 |

## The prompt

The session was landing the open pull requests on the maintainer's
instruction ("continue till the entire list is landed"). #178's Ubuntu build
had failed on `PropertyTests.time_on_timeline_agrees_with_the_runtime_calendar`
in `Varve.Xsd.Tests`, which #178 does not touch. The session reproduced it
on `main` with the run's seed, filed #211, and fixed it as its own pull
request from `main`.

## The report

**What failed.** With `CsCheck_Seed=6EntITDEea52`, two oracle tests throw
inside the oracle: `new DateTimeOffset(9999, 12, 31, 17, 45, 5, -12:47)`
is refused, because its UTC instant lies past year 9999. The second test,
`adding_a_day_time_duration_agrees_with_the_runtime_calendar`, fails the
same way with the same seed. Both reproduce on `main` at 826d67b9.

**Cause.** `OracleDateTime` drew years 1 to 9999 with any offset from
-14:00 to +14:00, though its own summary says "years the runtime's calendar
covers". The runtime holds UTC instants in years 1 to 9999 only, so the
first hours of year 1 under a positive offset and the last hours of year
9999 under a negative one have no runtime value. The property was never
reached; the oracle threw. A flake by seed on every branch, not a defect in
`Varve.Xsd`.

**Fix.** The oracle draws years 2 to 9998, and its summary says why. One
generator in `tests/Varve.Xsd.Tests/PropertyTests.cs`; no behaviour was
decided, so no ADR changes.

**Verification.** `Varve.Xsd.Tests.PropertyTests`, all nineteen, pass with
the seed and without it.
