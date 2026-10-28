---
set: handover-automation
namespace: varve
adr: 0181
decisions:
  - key: LivenessIsTheOrchestratorsJudgement
    statement: "Varve never judges a writer's liveness: the orchestrator's probe does, on its own schedule and threshold, and automation turns its verdict into a takeover request with no delay or retry of its own"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: TakeoverCommandIsIdempotent
    statement: "varve manage takeover --from <instance> takes over only an epoch that instance still holds, and reports a dataset held by another instance, an epoch that moved, or no automatic handover as needing nothing, with exit code 0"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: StaleTriggerAsksTheFailedInstance
    statement: "Before a takeover from an instance the record still names, the command asks it at its recorded address within five seconds; if it answers ready and holds the dataset as its writer, the trigger is stale and nothing is sent; no answer or not ready, and the takeover goes on"
  - key: TakeoverCommandAsksEachServerForItself
    statement: "For each dataset the command sends the takeover, at the epoch that server reported, to the first --server that is a warm standby of it, skipping one that does not answer or refuses; with no dataset named it takes every dataset listed and _fleet"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: AutomationIdentityHoldsOperatorWithoutSecret
    statement: "The automation's token holds the operator role on the management audience and is federated: a service account token by --client-assertion, or the platform's managed identity by --managed-identity; no key, secret or SAS"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: TwoAppLayoutKeepsRestart
    statement: "The default two-app Container Apps layout keeps restart as its recovery and gets no automation; its write downtime is measured beside the takeover times in the handover (services) job's summary"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: HandoverLayoutIsNSingleReplicaApps
    statement: "The Azure handover layout is N Container Apps apps of one replica each, every one a member of the system dataset's writer set, behind Front Door Standard with /health/ready as its probe"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: AzureDetectionIsAMetricAlertPerApp
    statement: "On Azure a crash is recovered by Container Apps' own restart; a metric alert per app on Replicas below 1 detects a replica that is gone, and a log search alert on the environment's readiness-probe failures detects one running but stuck not ready, both into one action group"
  - key: AzureChainAlertSecureWebhookLogicAppJob
    statement: "The alert's action group calls a Consumption Logic App by Secure webhook, never its Logic Apps action; the Logic App starts the alerting app's own takeover job, whose fixed arguments name the failed app and the others"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: LogicAppAcceptsOnlyTheActionGroup
    statement: "The Logic App's trigger has SAS disabled and one OAuth policy requiring the tenant's issuer, the operator's app registration as audience and the AzNS AAD Webhook's application id as azp; its identity holds only jobs read, start and executions read on the jobs"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: KubernetesSampleWatchesReadiness
    statement: "The Kubernetes controller is Varve's own image running varve manage watch-kubernetes: it watches the fleet's pods through the API server with its service-account token and no timer, and when one's Ready turns from True, takes over from it with varve manage takeover's own code; its role may only read and watch pods"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: KubernetesManifestsValidatedByKubeconform
    statement: "CI validates the Kubernetes sample's manifests with kubeconform, strict, against a pinned schema version, from an image listed in .github/ci-images.yaml"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: AspireKeepsRestart
    statement: "Varve.Aspire gains no handover automation: an AppHost's development loop recovers by restart"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
---

The rulings of [ADR 0181](../adr/0181-handover-automation.md), one line each.
Filed unaccepted by Replication part 1, #165 (ADR 0066); every citation is
`CS0618` until the maintainer accepts them.
