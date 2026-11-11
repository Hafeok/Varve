---
set: problems-that-say-what-to-do
namespace: varve
adr: 0242
decisions:
  - key: ForbiddenNamesWhatWasNeeded
    statement: "A 403 forbidden names the permission checked, its scope (a dataset, named, or the fleet), the plane, and the caller's agent as resolved from the token; it names no grant, role or other agent, and a 401 stays thin"
  - key: RefusalsNameOnlyWhatTheCallerSent
    statement: "A 403 names only what the caller sent, never anything looked up, and never confirms that something hidden exists: a graph or row the caller cannot see answers as it did before, and a hidden target answers exactly as an absent one"
  - key: FailuresAreClassifiedByWhetherARetryHelps
    statement: "A dataset that failed to open carries failure damaged, configuration or transient, read from exception types, on its entries and on the unavailable problem, whose detail says what to do"
  - key: SupersededNamesTheHolder
    statement: "writer-superseded names the writer and epoch the store's writer record holds, read when the write is refused, whatever the refused instance believes its role is; a fleet of one names none"
  - key: ShuttingDownSaysTheOthersServe
    statement: "shutting-down's detail says the instance is draining and, in a fleet, that the other instances keep serving"
  - key: EveryProblemPageSaysWhatToDo
    statement: "Every page in docs/problems/ has a What to do section, and a test fails a page without one"
---

The rulings of [ADR 0242](../adr/0242-problems-that-say-what-to-do.md),
filed unaccepted by the session for #194 (ADR 0066).
