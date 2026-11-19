---
set: problems-that-say-what-to-do
namespace: varve
adr: 0242
decisions:
  - key: ForbiddenNamesWhatWasNeeded
    statement: "A 403 forbidden names the permission checked, its scope (a dataset, named, or the fleet), the plane, and the caller's agent as resolved from the token; it names no grant, role or other agent, and a 401 stays thin"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-16T00:00:00Z
  - key: RefusalsNameOnlyWhatTheCallerSent
    statement: "A 403 names only what the caller sent, never anything looked up, and never confirms that something hidden exists: a graph or row the caller cannot see answers as it did before, and a hidden target answers exactly as an absent one"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-16T00:00:00Z
  - key: FailuresAreClassifiedByWhetherARetryHelps
    statement: "A dataset that failed to open carries failure damaged, configuration or transient, read from exception types, on its entries and on the unavailable problem, whose detail says what to do"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-16T00:00:00Z
  - key: SupersededNamesTheHolder
    statement: "writer-superseded names the instance that holds the epoch by its name in the fleet, and the epoch, as the store's writer record holds them, read when the write is refused, whatever the refused instance believes its role is; the data plane never names an instance's internal address, which stays on the management plane; a fleet of one names none"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-16T00:00:00Z
  - key: ShuttingDownSaysTheOthersServe
    statement: "shutting-down's detail says the instance is draining and, in a fleet, that the other instances keep serving"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-16T00:00:00Z
  - key: EveryProblemPageSaysWhatToDo
    statement: "Every page in docs/problems/ has a What to do section, and a test fails a page without one"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-16T00:00:00Z
  - key: DefectIsItsOwnClass
    statement: "Amended 2026-11-19: a failure no repeat and no setting can cure, whose cause is the code, is classed defect, not configuration, and its problem's detail and page say to report it with the log and upgrade; a binding stopped by a failure is the first"
---

The rulings of [ADR 0242](../adr/0242-problems-that-say-what-to-do.md),
filed unaccepted by the session for #194 (ADR 0066) and accepted on #267 on
2026-11-16, at the maintainer's word, by his delegate (ADR 0239).
