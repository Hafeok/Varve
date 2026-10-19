---
set: varve-aspire
namespace: varve
adr: 0117
decisions:
  - key: HostingIntegrationReferencesNothingFromVarve
    statement: "Varve.Aspire references Aspire.Hosting and nothing from Varve; it declares ArchLayer 0 by dependency and is a host concern by purpose, registered under constraint 4"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: AddVarveAddsTheContainerResource
    statement: "builder.AddVarve adds the ghcr.io/hafeok/varve container resource with the datasets volume, port 8080, OTLP wiring, /health/ready as the health check and the OIDC settings as parameters; without WithOidc it runs anonymous in run mode and refuses to publish"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: SampleNotShipped
    statement: "The sample AppHost and its test are outside the packable set, so the AppHost SDK's platform binaries fall under ADR 0009's shipped-artefact scope as the benchmark harness does"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: DatasetSetupRunsVarveApply
    statement: "WithDataset takes an optional setup file and runs varve setup apply from the server's image against the healthy server, authenticated by WithSetupClient under WithOidc; Varve.Aspire still references nothing from Varve; amended 2026-10-13 under ADR 0131"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: MessagePackPinnedPastTheAdvisory
    statement: "MessagePack, which Aspire.Hosting brings into Varve.Aspire's and the sample's closures, is pinned by central transitive pinning to 2.5.303, past GHSA-qhrr-8q5h-9q3h, scoped to src/Varve.Aspire/ and samples/, until Aspire.Hosting's own dependency moves past it; amended 2026-10-16"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-16T00:00:00Z
---

The rulings of [ADR 0117](../adr/0117-varve-aspire.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.

Moved to a later set: `WithDatasetDeclaresTheDataset` is superseded by ADR 0163, under which a dataset is declared by `varve apply`, in its set.
