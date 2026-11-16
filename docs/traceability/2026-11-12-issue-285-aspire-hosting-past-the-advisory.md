# `Aspire.Hosting` moves past its advisory, and the `MessagePack` pin leaves

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#285](https://github.com/Hafeok/Varve/issues/285); found while landing [#280](https://github.com/Hafeok/Varve/pull/280) and regenerating lockfiles on [#250](https://github.com/Hafeok/Varve/pull/250) |
| **Date** | 2026-11-12 |
| **Tool** | Claude Code, the maintainer's delegate session on his machine (ADR 0239) |
| **Model** | `claude-fable-5-2`, from the session's own metadata |
| **Session identifier** | `99e6699f-a3f7-48ac-974c-711ade42f85c` |
| **Branch** | `fix/aspire-hosting-13.6.5`, from `main` at 3da8929c |
| **ADRs** | [0117](../adr/0117-varve-aspire.md) (amended 2026-11-12, no new ruling) |

## The prompt

> continue

The maintainer's standing queue was the landing of #280, #281 and the pull
requests after them. No prompt asked for this fix. The session found every
build red while working that queue, filed #285 with the cause, and prepared
this branch on its own judgment so the queue can move once the maintainer
approves it.

## The report

- **The failure**, on #280 at 2efcad8d with `main` at 3da8929c merged in (CI
  run 43176536251), and in a local `dotnet run eng/lockfiles.cs`: `NU1903`,
  `Aspire.Hosting` 13.6.1 has a known high-severity vulnerability,
  GHSA-f7vh-h5g3-7jrh (CVE-2026-120006), published 2026-11-10 and patched in
  13.6.5.
- **The change.**
  - `Aspire.Hosting`, `Aspire.Hosting.AppHost` and `Aspire.Hosting.Testing` in
    `Directory.Packages.props`, and `Aspire.AppHost.Sdk` in the sample, move
    from 13.6.1 to 13.6.5. nuget.org has 13.6.5 for all of them.
  - The `MessagePack` 2.5.303 register entry is removed. `Aspire.Hosting`
    13.6.5 depends on 2.5.307, and keeping the pin failed restore with
    `NU1109`, a downgrade. ADR 0117's amendment of 2026-10-16 says the pin
    leaves at this point.
  - Three lockfiles are regenerated with `dotnet run eng/lockfiles.cs`:
    `src/Varve.Aspire`, the sample and the sample's test.
  - ADR 0117 gets a dated amendment recording both.
- **Run here, all passing:** `eng/lockfiles.cs` and its `--check`,
  `eng/dependency-register.cs`, `eng/decision-sets.cs`, `eng/sdk-pin.cs`,
  `eng/release-pending.cs`, and `dotnet build Varve.slnx` with 0 warnings and
  0 errors.
- **Not run here:** the tests, and the Aspire sample's test, which starts
  containers. CI on the pull request runs them.

## Questions for the maintainer

- `Varve.Aspire` is a shipped package, and v0.1.0-preview.20 depends on
  `Aspire.Hosting` 13.6.1. Does this bump ship as its own release, and what do
  its notes say about the advisory? This branch carries no descriptor.
- Is `MessagePackPinnedPastTheAdvisory` revoked now that its pin has left
  (ADR 0062), or kept as the record of a condition that was met?

## Deferred

- Nothing.
