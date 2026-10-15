---
set: the-register-for-events
namespace: varve
adr: 0146
decisions:
  - key: EventHubsSdkInSamplesOnly
    statement: "Azure.Messaging.EventHubs is admitted scoped to samples/"
  - key: JsonSchemaNetInTestsOnly
    statement: "JsonSchema.Net 8.0.5, the last release under the MIT licence alone, is admitted scoped to tests/; it ships no native asset"
  - key: AsyncApiSchemaVendored
    statement: "The AsyncAPI 3.0.0 JSON Schema is vendored as test data with its Apache-2.0 notice and never fetched at test time"
  - key: EmulatorImagesFromMcr
    statement: "The Event Hubs emulator and Azurite run in CI from mcr.microsoft.com, pinned by digest in .github/eventhubs/compose.yaml"
---

The rulings of [ADR 0146](../adr/0146-the-register-for-events.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration, part 2,
of #93 (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
