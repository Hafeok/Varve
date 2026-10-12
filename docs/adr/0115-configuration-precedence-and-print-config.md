# 0115 — Configuration from file, environment and command line; `--print-config`; an unknown key refuses to start

## Status

**Accepted — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Acceptance is the maintainer's act on the pull request.
**Amends [0101](0101-the-server-configuration-aot-shutdown-readiness.md)**
point 1 by a dated block, and `docs/operator/upgrade.md`'s "a setting this
build does not know is ignored by the binder".

## Context

ADR 0101 bound `Varve:` from `IConfiguration` with the ordinary providers:
`appsettings.json`, environment variables, command-line arguments in the
`--Varve:Key=Value` form. Two gaps remain for an operator. A misspelt setting
(`Varve:Limit:QueryTimeout`) is silently ignored and the default applies,
which is the opposite of what 0101 promised for a wrong value. And the
command line is the raw configuration syntax, where 7b's `System.CommandLine`
gives every other command typed options and help.

## Decision

1. **Precedence is file, then environment, then command line**, later
   winning, which is the order the providers are added in. The file is
   `appsettings.json` in the working directory, or the one `--config <path>`
   names (several `--config` flags layer in order). Environment variables map
   with `__` (`VARVE__LIMITS__QUERYTIMEOUT`).
2. **`varve serve` maps its options to configuration keys through
   `System.CommandLine`**: `--datasets-root`, `--dataset <name>[=File|Memory]`
   (repeatable), `--auth-mode`, `--authority`, `--audience` (repeatable),
   `--anonymous` (shorthand for `--auth-mode Anonymous`), `--urls`,
   `--config`, and `--set Key=Value` for any key under `Varve:`. The generic
   `--Varve:Key=Value` form stays accepted, because 7a's smoke, the tests and
   every existing script pass settings that way, and because it is the
   framework's own provider. `varve serve --help` lists the options.
3. **`varve serve --print-config`** prints the effective configuration under
   `Varve:` as JSON, every key with the value that would apply and the
   provider it came from, **secrets redacted**: a key whose last segment is
   `Secret`, `Password`, `Token` or `Key`, and the values of
   `OTEL_EXPORTER_OTLP_HEADERS`, print as `***`. It validates and exits 0,
   or prints the errors and exits 2, and never starts the server.
4. **An unknown key under `Varve:` is a startup error**, listed with the
   others and exiting 2: the host walks every section the configuration
   holds under `Varve` and compares each path against the known keys, which
   `SettingsCheck` holds as patterns (`Datasets:*:Storage`,
   `Auth:Datasets:*:Grants:*:Claim`, …). A test, by reflection in the test
   alone, asserts that every property of the settings classes is on the
   list, so a new setting cannot be added without being known. A misspelt
   setting is therefore an error, not a silently applied default.
5. **Keys outside `Varve:`** (`Logging`, `Kestrel`, `urls`, `OTEL_*`) are
   the framework's and are not checked.

## Alternatives considered

- **Replace the `--Varve:` form with options only.** Breaks every existing
  invocation for no gain; the two forms write to one configuration.
- **A JSON schema for `appsettings.json`.** Editor help, but it does not
  stop a wrong environment variable, and a schema is a second description of
  the settings classes.
- **Warn on an unknown key.** A warning in a log nobody reads at start is
  how a typo reaches production.

## Consequences

- `docs/operator/configure.md` states the precedence, every key, every
  option and the redaction rule; `upgrade.md`'s sentence about ignored keys
  is corrected.
- `Varve.Server`'s `Serve` command grows; `SettingsCheck` gains the key
  walk.

## Checks

- **Checked against the accepted ADRs** (0001–0109). Touches **0101**
  (amended), **0105** (`System.CommandLine`, now used by `serve`). No
  conflict.
- **Layer ownership.** `Varve.Server`, layer 6.
- **Analyzer rule.** None.
- **Open questions owned.** None.
