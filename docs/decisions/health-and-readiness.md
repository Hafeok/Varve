---
set: health-and-readiness
namespace: varve
adr: 0113
decisions:
  - key: LivenessConsultsNoDataset
    statement: "GET /health/live answers 200 once the host is listening, consults no dataset, and stays 200 while the process serves"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: ReadinessFalseDuringDrain
    statement: "Readiness becomes false from ApplicationStopping, before the listener closes, while liveness stays true"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: HealthUnauthenticatedRateLimitedRevealsNoContent
    statement: "Both health endpoints are unauthenticated, rate-limited per client address by Varve:Health:RateLimit, and reveal dataset names and states and nothing from the log"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
---

The rulings of [ADR 0113](../adr/0113-health-and-readiness.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.

Moved to a later set: `ReadinessIsEveryDatasetOpenAndWithinLag` is superseded by ADR 0163, whose readiness adds the management position and the followers, in its set.
