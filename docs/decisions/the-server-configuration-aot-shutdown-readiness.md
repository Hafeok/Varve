---
set: the-server-configuration-aot-shutdown-readiness
namespace: varve
adr: 0101
decisions:
  - key: ConfigurationValidatedAtStart
    statement: "Configuration is bound by the source generator and validated at start, and an invalid configuration lists every error and refuses to start"
  - key: AnonymousRefusedInProduction
    statement: "Auth:Mode has no default, and anonymous mode with Auth:Production refuses to start"
  - key: ServerIsNativeAot
    statement: "Varve.Server publishes Native AOT with minimal APIs only and source-generated JSON, and the publish is a CI gate"
  - key: ClockInjectedAtTheRoot
    statement: "The server injects TimeProvider.System and the random source at the composition root"
  - key: GracefulShutdownDrains
    statement: "On SIGTERM the server refuses new writes, ends live feeds, drains in-flight requests and their pins, and disposes each dataset"
  - key: DisposeWaitsForTheSequencer
    statement: "Dataset.DisposeAsync waits for the sequencer, so a commit in progress finishes before the dataset closes"
  - key: NoSealOnShutdown
    statement: "The active segment is not sealed on shutdown"
  - key: ReadinessIsProjectionAtHead
    statement: "GET /ready is 200 only when every dataset is open, not failed and at the head of its default projection; GET /live is 200 while serving"
  - key: StatusIsTheAdminEndpoint
    statement: "GET /datasets/{name}/status, the one admin endpoint of 7a, reports the dataset's id, head, durability, settings, checkpoints and failure"
  - key: ForwardedHeadersOnlyWhenConfigured
    statement: "Forwarded headers are honoured only when configured and only from configured proxies"
---

The rulings of [ADR 0101](../adr/0101-the-server-configuration-aot-shutdown-readiness.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
