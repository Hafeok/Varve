---
set: handover-automation
namespace: varve
adr: 0181
decisions:
  - key: LivenessIsTheOrchestratorsJudgement
    statement: "Varve never judges a writer's liveness: the orchestrator's probe does, on its own schedule and threshold, and automation turns its verdict into a takeover request with no delay or retry of its own"
  - key: TakeoverCommandIsIdempotent
    statement: "varve manage takeover --from <instance> takes over only an epoch that instance still holds, and reports a dataset held by another instance, an epoch that moved, or no automatic handover as needing nothing, with exit code 0"
  - key: TakeoverCommandAsksEachServerForItself
    statement: "For each dataset the command sends the takeover, at the epoch that server reported, to the first --server that is a warm standby of it, skipping one that does not answer or refuses; with no dataset named it takes every dataset listed and _fleet"
  - key: AutomationIdentityHoldsOperatorWithoutSecret
    statement: "The automation's token holds the operator role on the management audience and is federated: a service account token by --client-assertion, or the platform's managed identity by --managed-identity; no key, secret or SAS"
  - key: TwoAppLayoutKeepsRestart
    statement: "The default two-app Container Apps layout keeps restart as its recovery and gets no automation; its write downtime is measured beside the takeover times in the handover (services) job's summary"
  - key: HandoverLayoutIsNSingleReplicaApps
    statement: "The Azure handover layout is N Container Apps apps of one replica each, every one a member of the system dataset's writer set, behind Front Door Standard with /health/ready as its probe"
  - key: AzureDetectionIsAMetricAlertPerApp
    statement: "On Azure a metric alert per app on RestartCount detects a failed writer; the log search alert on ContainerAppSystemLogs is the documented alternative for a readiness failure that never restarts the replica"
  - key: AzureChainAlertSecureWebhookLogicAppJob
    statement: "The alert's action group calls a Consumption Logic App by Secure webhook, never its Logic Apps action; the Logic App starts the alerting app's own takeover job, whose fixed arguments name the failed app and the others"
  - key: LogicAppAcceptsOnlyTheActionGroup
    statement: "The Logic App's trigger has SAS disabled and one OAuth policy requiring the tenant's issuer, the operator's app registration as audience and the AzNS AAD Webhook's application id as azp; its identity holds only jobs read, start and executions read on the jobs"
  - key: KubernetesSampleWatchesReadiness
    statement: "The Kubernetes controller is Varve's own image running varve manage watch-kubernetes: it watches the fleet's pods through the API server with its service-account token and no timer, and when one's Ready turns from True, takes over from it with varve manage takeover's own code; its role may only read and watch pods"
  - key: KubernetesManifestsValidatedByKubeconform
    statement: "CI validates the Kubernetes sample's manifests with kubeconform, strict, against a pinned schema version, from an image listed in .github/ci-images.yaml"
  - key: AspireKeepsRestart
    statement: "Varve.Aspire gains no handover automation: an AppHost's development loop recovers by restart"
---

The rulings of [ADR 0181](../adr/0181-handover-automation.md), one line each.
Filed unaccepted by Replication part 1, #165 (ADR 0066); every citation is
`CS0618` until the maintainer accepts them.
