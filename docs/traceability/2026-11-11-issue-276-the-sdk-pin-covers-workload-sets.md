# The SDK pin covers workload sets, and no job bypasses it

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#276](https://github.com/Hafeok/Varve/issues/276); found while landing [#242](https://github.com/Hafeok/Varve/pull/242) for [#101](https://github.com/Hafeok/Varve/issues/101) |
| **Date** | 2026-11-11 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRvXwQujZvssWPK7ST`, the session of [the #101 record](2026-11-06-issue-101-repository-drift-and-federated-provider-credentials.md) |
| **Branch** | `claude/gifted-gauss-xyxhld-sdk-pin`, from `main` at 5c1965be |
| **ADRs** | [0224](../adr/0224-supply-chain-transparency.md) (amended 2026-11-11, filed unaccepted) |

## The prompt

> Go ahead: push a new branch and open the pull request, under a new issue (“CI installs a newer workload set and a job bypasses the SDK pin”). Scope:
>
> 1. `handover (services)` uses `./.github/actions/setup-dotnet`.
> 2. Pin the workload set to the one that ships with the SDK in `global.json`. Prefer `sdk.workloadVersion` in `global.json` if the .NET documentation confirms it does this; otherwise `dotnet workload install --version <set>` in the composite action. Say on the issue which you used and why. Don’t move the SDK or the lockfiles to 10.0.15.
> 3. A check, run from `eng/ci.cs`, that fails when a workflow uses `actions/setup-dotnet` directly or installs a workload outside the composite action. One failing fixture and one passing one.
> 4. A dated note in ADR 0224 that the pin covers workload sets, and a line in `docs/supply-chain.md` that an SDK bump moves the workload version and the lockfiles together.
>
> Post the head when it’s green. The delegate lands it first, ahead of the queue. Then merge `main` into #242 and carry on as planned.

## The report

- **The failures**, on `main` at 5c1965be (CI run 42981059065) and on #242:
  - `handover (services)` and `browser wasm` both failed with NU1004, 10.0.15 packages against lockfiles that record 10.0.14;
  - the .NET 10.0.15 servicing release had reached the runners that night.
- **`--version` in the composite action, not `sdk.workloadVersion` in `global.json`.** `workloadVersion` was tried first.
  - **What the documentation says.** Microsoft Learn's ".NET SDK workload sets" says both pin the set.
  - **Why `workloadVersion` was dropped.** It also makes every build need that set installed. #279's first push (86db7b2) failed the `publish` dry run, which installs no workload, with MSB4242: "Workload version 10.0.403, which was specified in global.json, was not found". Here, a class library under a `global.json` naming set 10.0.404 failed to build the same way. So it would break every job and developer machine that never installs workloads.
  - **What `--version` does.** `dotnet workload install wasm-tools wasm-experimental --version 10.0.403` installed set 10.0.403 with manifest 10.0.114 and the browser-wasm packs at 10.0.14. `dotnet workload --version` printed `10.0.403`, and a locked restore of `Varve.WebUi` passed.
  - **The set's contents**, read from nuget.org (`Microsoft.NET.Workloads.10.0.400`):
    - 10.403.0 (set 10.0.403) holds the Mono toolchain manifest 10.0.114, whose WebAssembly packs are 10.0.14;
    - 10.404.0 moves it to 10.0.115.
- **Every bypass, not only `handover (services)`.**
  - `release-gate.yml`'s four jobs and one `nightly.yml` job also used `actions/setup-dotnet` with the roll-forward. They now use the composite action.
  - `agent-review.yml`'s judge keeps its direct use, marked `sdk-pin: exempt`: it checks out `main` and restores nothing.
  - The four `dotnet workload install` steps in `ci.yml` and `publish.yml` are now the action's `workloads:` input. The action installs with `--version` set to `global.json`'s SDK version, and checks the set in use.
- **`eng/sdk-pin.cs`**, with the jobs `sdk-pin` and `sdk-pin-fixtures` in `eng/ci.cs`.
  - The failing fixture has three findings and exits 1. The passing one exits 0, and so does the repository.
- **Not moved:** the SDK stays at 10.0.403, and the lockfiles at 10.0.14.
- **Gates run here, all passing:** `sdk-pin`, `sdk-pin-fixtures`, `decision-sets`, `licence-headers`, `lockfiles`, `register`, and the locked restore of `Varve.WebUi`.

## Questions for the maintainer

- Accept the amendment's ruling, `CiInstallsTheWorkloadSetGlobalJsonPins`. Nothing cites it.

## Deferred

- Nothing. The action takes the workload version from `sdk.version`, so an SDK bump moves it with no other edit.
