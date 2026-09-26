---
set: repo-standard
namespace: varve
adr: 0039
decisions:
  - key: RepoStandardBuiltHere
    statement: "tools/repo-standard holds a CLI over a YAML declaration of repository settings and a composite GitHub Action, built here and not published from this repository"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: RepoStandardIsolated
    statement: "Nothing in src/ references repo-standard and it references nothing there, with its own solution and its own Directory.Build.targets"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: RepoStandardHeldToVarveRules
    statement: "While it lives here the tool is held to Varve's rules: warnings as errors, the MPL-2.0 header, register citations, issue references, sign-off and traceability"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: YamlDotNetParserAndEmitterOnly
    statement: "YamlDotNet is the tool's only runtime package, used through its parser and emitter into a JsonNode tree, with no Octokit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: TokenOnTheAuthorizationHeaderOnly
    statement: "The tool puts its GitHub token on the Authorization header only, never logs or writes it, and never sends it to an extends URL or follows a link off the API host"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: AppTokenForVarvesWorkflow
    statement: "Varve's own repo-standard workflow uses only a GitHub App installation token, and no long-lived credential is added"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: UnwritableSettingsReported
    statement: "Settings no API can write are read and reported or left out, never half supported"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
---

The rulings of [ADR 0039](../adr/0039-repo-standard.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
