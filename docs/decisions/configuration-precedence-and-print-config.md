---
set: configuration-precedence-and-print-config
namespace: varve
adr: 0115
decisions:
  - key: FileThenEnvironmentThenCommandLine
    statement: "Configuration comes from appsettings.json or the files --config names, then environment variables, then the command line, later winning"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: ServeOptionsMapToConfigurationKeys
    statement: "varve serve maps typed System.CommandLine options and --set Key=Value onto configuration keys under Varve:, and the generic --Varve:Key=Value form stays accepted"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: PrintConfigRedactsSecrets
    statement: "varve serve --print-config prints the effective Varve: configuration with its provider per key, redacting keys ending in Secret, Password, Token or Key and the OTLP headers, validates, and never starts the server"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: UnknownKeyRefusesToStart
    statement: "An unknown key under Varve: is a startup error listed with the others, checked against the known key patterns SettingsCheck holds, which a test keeps complete against the settings classes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
---

The rulings of [ADR 0115](../adr/0115-configuration-precedence-and-print-config.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
