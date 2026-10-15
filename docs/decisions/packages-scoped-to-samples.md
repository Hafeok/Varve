---
set: packages-scoped-to-samples
namespace: varve
adr: 0133
decisions:
  - key: RegisterScopeChecked
    statement: "A register entry's Scope is enforced by eng/dependency-register.cs over every project, with a failing and a passing fixture run by register-fixture"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: IdentityWebInSamplesOnly
    statement: "Microsoft.Identity.Web is admitted scoped to samples/"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: ServerOnlyEntriesScoped
    statement: "The register entries that said Varve.Server only carry that scope, checked"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
---

The rulings of [ADR 0133](../adr/0133-packages-scoped-to-samples.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration of #93
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.
