---
set: dataset-settings-as-a-commit-kind
namespace: varve
adr: 0021
decisions:
  - key: SettingsAreACommitKind
    statement: "Settings are a commit kind beside Data and Erasure, sequenced like any other commit with an agent, a cause and a position"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: SettingsAreAFoldOfTheLog
    statement: "A dataset's settings at P are a fold over the Settings commits up to P, a pure function of the log that travels with it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: SettingsCommitsHaveEmptyDelta
    statement: "A Settings commit carries an empty delta and is exempt from I4, like an Erasure commit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ErasureModeCannotBeTurnedOff
    statement: "The sequencer refuses to turn erasure mode off while any private entry exists in the dictionary"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ErasureModeNotRetroactive
    statement: "Turning erasure mode on governs what happens from its position onward and makes no earlier term private"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0021](../adr/0021-dataset-settings-as-a-commit-kind.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
