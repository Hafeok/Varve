# 0181 — Handover automation: the orchestrator judges liveness, one idempotent command acts, and the two-app layout keeps restart

## Status

**Accepted — filed unaccepted by Replication part 1 (#165), 2026-10-28**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on #97's plan, 2026-10-27:
- **Varve never judges liveness.** The orchestrator's probe does, and
  automation turns a probe failure into a takeover request with an
  operator-role identity (plan point 4);
- **what ships:** a Kubernetes sample whose manifests CI validates, and on
  Azure the N-single-replica layout behind Front Door, with its automation
  and its cost;
- **the Azure chain (Q4):** an alert, then a Consumption Logic App, then a
  Container Apps job:
  - the action group calls the Logic App by **Secure webhook** (Entra),
    never by its "Logic Apps" action, which calls the trigger's SAS
    callback URL;
  - the Logic App has SAS disabled, and an OAuth authorisation policy that
    accepts only the action group's service principal;
  - the Logic App's identity may only start the job, and the job's
    identity holds Operator;
  - the Entra app registration for the secure webhook is an
    operator-supplied prerequisite in the README, identified by id (the
    rule the maintainer named as the perf ADR's; that ADR is not on this
    branch);
- **detection:** a metric alert per app is compared with a log search alert,
  and the end-to-end detection time is measured on the optional leg (#164);
- **the default two-app layout keeps restart as its recovery**, and its
  write downtime is measured beside the takeover time (#97's amendment).

**Builds on** [ADR 0179](0179-writer-handover-within-a-store.md) point 6 (the
takeover request) and point 7 (no timers in Varve), and
[ADR 0180](0180-warm-standbys-and-the-writer-set.md) (the writer set).
**Adds to** [ADR 0142](0142-deployment-on-container-apps-and-small-machines.md)
a second Container Apps layout. The default it decided stands.

Records decision set
[`handover-automation`](../decisions/handover-automation.md).

## Context

ADR 0179 has two triggers. Abdication is Varve's own: a writer that shuts
down, or meets a storage error, releases its epoch, and a warm standby
claims it. A writer that crashes, hangs or loses its machine releases
nothing. Its epoch moves only when a takeover request names it, and ADR 0179
leaves the request's sender to this ADR. Varve does not decide that a writer
is dead, because nothing it observes says so: a slow writer, a partitioned
client and a dead writer look alike from inside (ADR 0179 point 6).

An orchestrator already judges liveness. Kubernetes' kubelet and Container
Apps both run `/health/live` and `/health/ready` on their own schedule,
with their own failure thresholds. What remains is to turn that judgement
into one takeover request, safely and with no secret. A request that
arrives late, twice, or after the writer came back must change nothing.

## Decision

**1. The orchestrator judges liveness, and Varve gains no timer.** The
probe's schedule and threshold are the orchestrator's (ADR 0179 point 7).
The automation acts on the probe's verdict, and does not add a delay or a
retry of its own.

**2. One command is the act: `varve manage takeover`.**

```sh
varve manage takeover [<dataset>…] --from <instance> --server <url> [--server <url>…]
```

- **`--from` names the instance the probe found failed.** Only an epoch it
  still holds is taken over: each dataset's state names the epoch's holder
  (ADR 0180 point 7).
- **With no datasets named, every dataset is taken.** That is every dataset
  the first answering server lists, and the system dataset, `_fleet`.
- **Each `--server` is an instance that may become the writer**, in order of
  preference. Every instance answers a takeover for itself, at the epoch it
  reads (ADR 0179 point 6). So, for each dataset, the command asks the first
  server that is a warm standby of it, and sends `{"takeover":{"epoch":n}}`
  at the epoch that server reported. It skips a server that does not
  answer, the failed one among them, and one that refuses.
- **It is idempotent.** Each of these is reported as needing nothing, with
  exit code 0:
  - a dataset another instance holds;
  - a dataset whose epoch moved meanwhile (`epoch-moved`);
  - a dataset that declares no automatic handover (`handover-off`), which
    keeps restart as its recovery.

  So a late, duplicated or false trigger is a no-op. A false one that lands
  while the writer is alive is an unnecessary handover, which ADR 0179
  point 7 already shows is safe.
- **Exit code 1** means some dataset was refused by every server named, for
  example with no warm standby. Standard error names each refusal.

**3. The automation's identity holds the operator role, and no secret.**
- The command takes a token by any of the CLI's ways, as `varve apply`
  does (ADR 0163).
- The automation uses a federated one: a Kubernetes service account token,
  as `--client-assertion <file>`, or, new here, `--managed-identity`. That
  option asks the platform's managed-identity endpoint for the management
  audience. In Container Apps that endpoint is `IDENTITY_ENDPOINT`, the
  source `Varve.Store.Azure` already reads for the store.
- No key, secret or SAS is created or read (ADR 0137).

**4. The default two-app layout keeps restart as its recovery** (ADR 0142).
- Its writer app has at most one replica, and the platform restarts it. The
  restarted instance finds the record naming itself and claims at once (ADR
  0180 point 3).
- The write downtime is measured beside the takeover times, in the handover
  (services) job's summary: from the writer's stop, through its start
  again, to the first write a follower forwards. Locally, three runs each:

  | Recovery | Azurite | RustFS |
  |---|---:|---:|
  | Restart, two-app layout, no standby | 2,568–3,221 ms | 1,998–2,264 ms |
  | Takeover by a warm standby, shutdown abdication | 908 ms | 573 ms |
  | Takeover by a warm standby, probe request | 504 ms | 312 ms |

- The restart's figure includes the process start. A platform's own restart
  adds its time to it, and #164 measures that on real Container Apps.
- The layout needs no automation, and gets none.

**5. The handover layout on Azure: N single-replica apps behind Front Door**
(`deploy/azure/container-apps-handover/`).
- **N apps**, each `minReplicas 1` and `maxReplicas 1`. Each app is one
  instance, its name the app's name, and each has its own identity and
  `EmptyDir` cache.
- **Every app is a member** of the system dataset's writer set
  (`Varve:Fleet:Writer` lists them all, and `Varve:Fleet:AutomaticHandover`
  is true). Datasets are declared with `placedOn` and `standbyOn` among them.
- **Front Door Standard** spreads clients over the N apps, with
  `/health/ready` as its health probe. Writes are forwarded between members
  on their internal addresses (ADR 0168).
- **The cost is N always-on replicas**, against the two-app layout's one
  writer and followers that scale to zero, plus Front Door's base fee. The
  Bicep's README states both, confirmed against the pricing pages when it is
  written.

**6. Detection on Azure: a metric alert per app.**
- **The signal.** Each app's alert fires on its `RestartCount` metric
  increasing. That metric is a platform metric at one-minute grain, and
  counts the restarts a failed liveness probe or a crash causes. A
  restarted writer reclaims at once (point 4), so a takeover the alert
  starts either wins, if the restart is slow, or finds the epoch moved and
  does nothing (point 2).
- **The comparison.**

  | | Metric alert per app | Log search alert |
  |---|---|---|
  | Signal | `RestartCount` (and `Replicas`), platform metrics | `ContainerAppSystemLogs` rows of probe failures |
  | Most frequent evaluation | every minute | every minute |
  | Data latency, per Azure Monitor's documentation | under a minute | resource logs, typically 3–10 minutes end to end |
  | Price, West Europe, pay-as-you-go | $0.10 per time series per month; the first 10 free | $3.00 per rule per month at one minute, $1.50 at five |
  | Also needs | nothing | Log Analytics ingestion of system logs, $2.99 per GB after the first 5 GB |

  The metric alert is chosen. It is earlier by the logs' ingestion delay,
  and is free at up to ten apps. The log search alert sees a readiness
  failure that never restarts the replica. The operator guide gives it as
  the alternative for that case.
- **The measured end-to-end detection time**, from a writer's failure to
  the job's start, is the optional leg's (#164). No figure is claimed here
  until it is measured.

**7. The Azure chain: alert, Secure webhook, Consumption Logic App,
Container Apps job.**
- **The action group** calls the Logic App by a **Secure webhook**, never by
  its "Logic Apps" action, which calls the trigger's SAS callback URL. Azure
  Monitor issues the webhook's token as its *AzNS AAD Webhook* service
  principal (`461e8683-5575-4561-ac7f-899cc907d62a`), for the operator's app
  registration. The operator creates that registration, gives the service
  principal its app role, and passes its id to the Bicep. It is not the
  template's to create (Q4).
- **The Logic App**, Consumption, has an HTTP request trigger:
  - SAS is disabled (`accessControl.triggers.sasAuthenticationPolicy.state:
    Disabled`);
  - its one OAuth authorisation policy requires the issuer
    `https://login.microsoftonline.com/{tenant}/v2.0`, the audience of the
    operator's app registration, and `azp`, the AzNS AAD Webhook's
    application id. A token must match every claim, so no other caller
    passes.
- **The Logic App's action** starts the job named for the alerting app,
  `{app}-takeover`, with
  `POST …/Microsoft.App/jobs/{app}-takeover/start?api-version=2026-07-01`,
  under its system-assigned identity. It passes nothing to the job: each
  job's arguments are fixed at deployment.
- **The Logic App's role** is a custom role scoped to the jobs alone:
  `Microsoft.App/jobs/read`, `Microsoft.App/jobs/start/action` and
  `Microsoft.App/jobs/executions/read`. The built-in Container Apps Jobs
  Operator role would also grant `listSecrets`.
- **A start may override the job's template.** So whoever may start the job
  may run a container under its identity, and the Logic App's identity is,
  in effect, as strong as the job's. The OAuth policy is therefore the
  trust boundary: only the action group's service principal reaches the
  Logic App at all. The operator guide says so.
- **The jobs.** There is one per app, manual trigger, Varve's image. Each
  runs `varve manage takeover --from {app} --server …` over the other apps'
  internal addresses, in order. Its user-assigned identity holds Operator
  on the management audience, by `--managed-identity`.
- **The chain's own cost** is a few cents a month at most:
  - Logic Apps Consumption bills each built-in action at $0.000025, after
    the first 4,000 free a month;
  - a secure webhook notification is $6 per million;
  - a 30-second job at 0.25 vCPU and 0.5 GiB is about $0.0003 before the
    Container Apps free grant.

**8. The Kubernetes sample** (`deploy/kubernetes/`).
- **A StatefulSet** of N pods. Each pod is one instance, named for itself by
  the downward API, with its own volume for the cache, `/health/live` and
  `/health/ready` as probes, and a headless Service for internal addresses.
- **The handover controller is Varve's own image**, running `varve manage
  watch-kubernetes`, one replica:
  - **It watches** the StatefulSet's pods through the API server, by their
    label selector, with the pod's service-account token, over a raw
    `HttpClient` that trusts the service account's CA alone. It lists
    them, then watches from the list's version; the API server pushes each
    change, so nothing waits on a clock. A watch the server ends is opened
    again from the last version seen. One refused as too old (`410`) starts
    from a fresh list, and one that fails is opened again after a pause.
  - **When a pod's `Ready` turns from `True`** to anything else, after
    kubelet's own `failureThreshold`, or the pod is deleted while ready, it
    takes over from that pod over the other pods' addresses. It calls the
    same code as `varve manage takeover --from` (point 2), in-process.
  - **It reads the API server's JSON with `JsonDocument`**, as every JSON
    reader in `src/` does. The plan's source-generated JSON was tried: in a
    packable project the generator's metadata code calls
    `Type.GetConstructor` and `Type.GetProperty`, which ADR 0064's banned
    symbols refuse, 19 errors. `JsonDocument` needs no reflection either.
- **A rolling restart** turns pods not ready too. Its abdication has moved
  the writer already, so the takeover finds nothing to do (point 2).
- **RBAC.** The controller's service account may `get`, `list` and `watch`
  pods, in its namespace and no more. Its federated token is exchanged at
  the identity provider for the operator role.
- **CI validates the manifests** with `kubeconform`, strict, against a
  pinned Kubernetes schema version. Its image is a row of
  `.github/ci-images.yaml`, mirrored like every other.

**9. Aspire keeps restart.** An AppHost runs a development loop on one
machine, where a restart is the recovery and a probe-driven takeover proves
nothing. `Varve.Aspire` gains no automation.

## Consequences

- A probe-driven takeover is one command, the same on every orchestrator.
  Its safety is ADR 0179's, not the automation's: the request is guarded by
  the epoch it names, and the command treats every refusal that means
  "already handled" as success.
- An operator chooses between restart, which is free and measured, and the
  handover layout, whose standing cost is N replicas and Front Door's fee.
- The secure webhook's app registration is a step the operator takes
  before deploying, as the entra template's deployer already is.
- The detection time on Azure stays unmeasured until the optional leg
  measures it (#164).

## Considered and rejected

- **The action group's "Logic Apps" action.** It calls the trigger's SAS
  callback URL, a static key (Q4).
- **An Azure Function on the alert.** It needs a plan, storage and a
  deployment of code, where a Consumption Logic App starts one job and
  holds no code.
- **One job given the failed app as an input.** That needs a template
  override on each start, which the Logic App's role allows anyway. Fixed
  arguments per app keep each run auditable from its job's definition.
- **A watch loop or a lease inside Varve.** That would be a timer in Varve's
  decisions (ADR 0179 point 7).
- **A Kubernetes operator with its own custom resource.** It would be more
  code than the act it performs. One watch and the takeover command's own
  code show the shape, and an operator may replace it with their own.
- **A shell controller on a community image**, `kubectl` and `jq` creating
  Jobs from a template. A third-party image holding RBAC over the writer is
  the wrong place for a dependency, and the takeover would run in a second
  image (the maintainer, on #175).
