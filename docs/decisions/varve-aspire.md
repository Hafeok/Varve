---
set: varve-aspire
namespace: varve
adr: 0117
decisions:
  - key: HostingIntegrationReferencesNothingFromVarve
    statement: "Varve.Aspire references Aspire.Hosting and nothing from Varve; it declares ArchLayer 0 by dependency and is a host concern by purpose, registered under constraint 4"
  - key: AddVarveAddsTheContainerResource
    statement: "builder.AddVarve adds the ghcr.io/hafeok/varve container resource with the datasets volume, port 8080, OTLP wiring, /health/ready as the health check and the OIDC settings as parameters; without WithOidc it runs anonymous in run mode and refuses to publish"
  - key: WithDatasetCreatesThroughTheAdminApi
    statement: "WithDataset creates the dataset at start through PUT /datasets/{name}, idempotently"
  - key: SampleNotShipped
    statement: "The sample AppHost and its test are outside the packable set, so the AppHost SDK's platform binaries fall under ADR 0009's shipped-artefact scope as the benchmark harness does"
---

The rulings of [ADR 0117](../adr/0117-varve-aspire.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
