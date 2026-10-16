# 0146 — The register for events: the Event Hubs SDK in a sample, a schema validator in tests, and the emulator's images

## Status

**Accepted — filed unaccepted by milestone Client and integration, part 2, of
#93, 2026-10-15** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer in the part 2 brief, point 9, and on its
plan: the Event Hubs bridge is a sample; a JSON Schema validator is admitted
for tests only and must be shown to ship no native assets; the emulator and
the Azurite it needs come from `mcr.microsoft.com`, pinned by digest (ADR 0099).

## Context

Two packages and two images are needed, and none may reach a shipped closure.

- **The bridge sample** shows an application publishing rendered CloudEvents
  (ADR 0143) to Azure Event Hubs. It is written with the SDK an application
  would use, and run against the Event Hubs emulator in CI.
- **The gate** asks that every rendered event validate against its generated
  schema, and that the AsyncAPI document validate against the AsyncAPI 3.0
  schema, which is JSON Schema draft-07 with many references. Validating that
  needs a real validator. Writing one for test use alone would be a large
  thing to own and to get wrong.

## Decision

1. **`Azure.Messaging.EventHubs` 5.12.2** (MIT) is admitted, `Scope="samples/"`
   (ADR 0133).
   - It is used by `samples/Varve.EventHubs.Sample` and its tests.
   - Its closure (`Azure.Core`, `Azure.Core.Amqp`, `Microsoft.Azure.Amqp`)
     is managed and MIT, and comes with it into the sample and nowhere else.
   - An Event Hubs package in `src/` stays a non-goal until a second consumer
     wants one.
2. **`JsonSchema.Net` 8.0.5** (MIT) is admitted, `Scope="tests/"`.
   - It is used by `tests/Varve.Events.Tests` and `tests/Varve.Protocol.Tests`
     to validate events against their schemas and the AsyncAPI document
     against its schema.
   - Its `lib/` holds `net8.0`, `net9.0`, `net10.0` and `netstandard2.0`
     assemblies and **no `runtimes/` folder**. Its closure (`JsonPointer.Net`
     6.0.0, `Json.More.Net` 2.2.0 and `Humanizer.Core` 3.0.1) is managed and
     MIT. The register's entry records it.
   - **Why not the current 9.x.** From 9.0.0 the binaries are published under
     the json-everything *Open Source Maintenance Fee* end-user agreement, which
     charges revenue-generating users, not under MIT alone. 8.0.5 is the last
     release whose package licence is the MIT expression. The pin is
     deliberate: `.github/dependabot.yml` ignores `JsonSchema.Net` versions
     `>= 9.0.0`, so the bot never proposes them, and that rule cites this
     ADR.
3. **The AsyncAPI 3.0.0 JSON Schema** is vendored as test data, from
   `asyncapi/spec-json-schemas` (Apache-2.0), under
   `tests/Varve.Events.Tests/fixtures/asyncapi/` with its licence notice.
   It is never fetched at test time.
4. **The images**, in the register's image table, are pinned by digest in
   `.github/eventhubs/compose.yaml`:
   - **`mcr.microsoft.com/azure-messaging/eventhubs-emulator` 2.2.1.** The
     licence is Microsoft's emulator licence, which the container accepts
     with `ACCEPT_EULA=Y`. It is run in CI, never linked or shipped.
   - **`mcr.microsoft.com/azure-storage/azurite` 3.37.0** (MIT), the
     emulator's checkpoint and metadata store.
5. **The CI job `samples-eventhubs`** starts the compose file, runs the
   sample's tests with `VARVE_EVENTHUBS_REQUIRED=1`, and stops the containers.
   Locally the test is skipped without `VARVE_EVENTHUBS_CONNECTION`, as the
   on-behalf-of leg is without its inputs.

## Alternatives considered

- **`JsonSchema.Net` 9.x.** Its licence terms are not the register's MIT. See
  point 2.
- **`NJsonSchema`.** It is MIT, but it supports draft-04 to draft-07 only, and
  the generated schemas are 2020-12.
- **`Corvus.JsonSchema`.** It is Apache-2.0 and generates code per schema. A
  test that validates many generated schemas would generate code for each.
- **Our own draft-07 and 2020-12 validator.** It would be thousands of lines to
  own, for test use alone.
- **Testcontainers for the emulator.** It is a new package and a new pattern.
  The repository starts its CI containers from compose files the workflow
  runs (ADR 0099), and so does this job.

## Consequences

- The sample uses what applications use, and the shipped closure is
  unchanged.
- The register's scope check (ADR 0133) proves that neither package reaches
  `src/`.

## Checks

- **Checked against the accepted ADRs** (0001–0133). It touches:
  - **0009** (the register);
  - **0099** (images by digest, from registries without anonymous pull
    limits);
  - **0133** (scopes);
  - **0025** and **0007** (test-time dependencies).

  No conflict.
- **Layer ownership.** None. `samples/` and `tests/`.
- **Analyzer rule.** None. The register's gate.
- **Open questions owned.** None.
