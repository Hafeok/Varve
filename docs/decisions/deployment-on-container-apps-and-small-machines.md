---
set: deployment-on-container-apps-and-small-machines
namespace: varve
adr: 0142
decisions:
  - key: ContainerAppsTemplateIsIdentityOnly
    statement: "deploy/azure/container-apps creates a storage account that refuses shared-key access and grants the app's system-assigned identity Storage Blob Data Contributor on its container alone; no key, SAS token or connection string is created"
  - key: OneReplicaAtMost
    statement: "The Container Apps template pins maxReplicas to 1 and a single active revision: a second replica would claim the epoch and supersede the first"
  - key: ProbesAreTheHealthEndpoints
    statement: "Liveness probes /health/live and readiness /health/ready, so a superseded or draining revision leaves the load balancer before it is stopped"
  - key: TemplatesBuildAndLintAlways
    statement: "Every template under deploy/azure is built and linted in the always-on CI run; what-if and a deployment run only in the optional Azure leg, signed in by GitHub's OIDC token"
  - key: AspireTakesTheContainerAsAnExpression
    statement: "Varve.Aspire's WithAzureBlobStorage takes the container's address as an expression and passes no credential; it adds no package"
  - key: SmallMachineFileBackendOnNvme
    statement: "On a small machine the supported store is the file backend on an NVMe SSD; an SD card's fsync and write endurance do not suit a log"
  - key: NasObjectStoreOnlyByIdentityOrSameHost
    statement: "An object store on a NAS is supported only with AssumeRoleWithWebIdentity, or on the same host as the server with its loopback key; a typical NAS offers neither"
---

The rulings of [ADR 0142](../adr/0142-deployment-on-container-apps-and-small-machines.md), filed unaccepted
by milestone Object storage of #92 (ADR 0066).
