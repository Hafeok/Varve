# Varve on Azure Container Apps: the handover layout

`main.bicep` deploys Varve's handover layout: N single-replica apps behind
Front Door, every one a member of the writer set, and the automation that
turns a failed member into a takeover request (ADRs 0179–0181;
[The fleet](../../../docs/operator/fleet.md), "Standbys: a writer set",
"Taking over from a failed writer", "Restart or handover"). The default
two-app layout, [`../container-apps`](../container-apps/README.md), keeps
restart as its recovery and needs none of this (ADR 0181 point 4).

## What it deploys

- **Shared:** a Log Analytics workspace, a storage account that **refuses
  shared-key access** with a private container, `datasets`, and a Container
  Apps environment whose system logs go to the workspace.
- **N apps**, `{name}-{instance}` for each of `instances` (`a`, `b`, `c` by
  default), each of the image of ADR 0111:
  - exactly **one replica** (`minReplicas` and `maxReplicas` 1), one revision
    at a time, external ingress on 8080, `/health/live` and `/health/ready`
    as probes, an `EmptyDir` cache;
  - `Varve:ObjectStorage:Instance` its instance, `Varve:Fleet:Writer` every
    instance in order (`a,b,c`), `Varve:Fleet:AutomaticHandover` true, and
    `Varve:Fleet:InternalUrl` its own address in the environment;
  - a system-assigned identity with **Storage Blob Data Contributor** on the
    container alone: any member may become the writer.
- **Front Door Standard**: one endpoint, one origin group of the N apps
  with `/health/ready` as its health probe (GET, HTTPS), and one route,
  `/*`, HTTPS only.
- **Takeover jobs**: one manual Container Apps job per app,
  `{name}-{instance}-takeover`, Varve's image, 0.25 vCPU and 0.5 GiB, no
  retry. Each runs, fixed at deployment:

  ```sh
  varve manage takeover --from a --server https://{name}-b.{domain} --server https://{name}-c.{domain} \
    --managed-identity --scope api://varve-manage/.default
  ```

  All jobs share one user-assigned identity, picked by `AZURE_CLIENT_ID`.
  The template makes it a bootstrap operator by its agent IRI,
  `https://login.microsoftonline.com/{tenantId}/v2.0#{its principal id}`
  (`Varve:Manage:BootstrapOperators`, ADR 0163 point 7), and sets
  `Varve:Auth:SubjectClaim` to `oid` so that the IRI names it.
- **Detection** (ADR 0181 point 6, amended 2026-10-28; the ruling
  `AzureDetectionIsAMetricAlertPerApp`), both into one action group:
  - **a replica that is gone**: a metric alert per app, `Replicas`
    (`Microsoft.App/containerApps`) below 1, minimum over one minute,
    evaluated every minute;
  - **a replica stuck not ready**: one log search alert on the workspace,
    readiness-probe failures in `ContainerAppSystemLogs_CL` per app
    (`ContainerAppName_s` as the dimension) over five minutes, evaluated
    every minute.
- **The action group**'s only receiver is a **Secure webhook** to the Logic
  App's trigger, with Entra authentication and the common alert schema.
  Never the action group's "Logic Apps" receiver, which calls the trigger's
  SAS callback URL.
- **A Consumption Logic App** with a system-assigned identity:
  - its HTTP request trigger refuses SAS
    (`sasAuthenticationPolicy.state: Disabled`), and one OAuth policy
    requires `iss` `https://login.microsoftonline.com/{tenantId}/v2.0`, `aud`
    the app registration's client id, and `azp`
    `461e8683-5575-4561-ac7f-899cc907d62a`, the AzNS AAD Webhook;
  - it acts only on `data.essentials.monitorCondition` `Fired`, takes the
    app from the log alert's `ContainerAppName_s` dimension or else the last
    segment of the metric alert's `alertTargetIDs[0]`, checks it is one of
    this template's apps, and starts `{app}-takeover` with
    `POST …/Microsoft.App/jobs/{app}-takeover/start?api-version=2026-07-01`,
    under its identity, with no body;
  - a **custom role**, `Microsoft.App/jobs/read`,
    `Microsoft.App/jobs/start/action` and `Microsoft.App/jobs/executions/read`,
    assigned on each job, not the resource group. The built-in Container
    Apps Jobs Operator would also grant `listSecrets`.

No key, SAS token or connection string is created or read (ADR 0137), except
the environment's own Log Analytics key, as in `../container-apps`.

## Prerequisites: the Secure webhook's app registration

Azure Monitor's action group presents a token issued to its *AzNS AAD
Webhook* service principal for an app registration you own. The template
takes it by id and does not create it (ADR 0181 point 7). Following
Microsoft's
[Configure authentication for Secure webhook](https://learn.microsoft.com/azure/azure-monitor/alerts/action-groups#configure-authentication-for-secure-webhook)
and its PowerShell script, as an Application Administrator:

1. **Register an application** for the webhook, single tenant, and under
   *Expose an API* set an **Application ID URI**, for example
   `api://varve-handover`.
2. **Accept v2.0 access tokens**: in the manifest set
   `requestedAccessTokenVersion` (`accessTokenAcceptedVersion`) to `2`. The
   Logic App's policy requires the v2.0 issuer and `azp`, which only v2.0
   tokens carry, and a v2.0 token's `aud` is the client id.
3. **Create the app role** `ActionGroupsSecureWebhook`, allowed member
   type *Application*.
4. **Give the AzNS AAD Webhook service principal that role**: create the
   service principal for application id
   `461e8683-5575-4561-ac7f-899cc907d62a` in the tenant if it is missing,
   then assign it the role on your registration's service principal. The
   script in the page above does steps 3 and 4.
5. Note the registration's **object id** (the application object, not its
   service principal), its **application (client) id** and its
   **Application ID URI**: `webhookObjectId`, `webhookClientId` and
   `webhookIdentifierUri`.

The page also says the identity that creates or tests the Secure webhook
action must be an **owner** of the app registration, so make the deployer
one.

The management plane's registration (`api://varve-manage`, see
[Authenticate](../../../docs/operator/authenticate.md)) must issue **v2.0
tokens** too, as it already does for Varve's issuer to match: the takeover
jobs' managed identity asks it for a token, and its agent IRI names the v2.0
issuer.

## Deploy

```sh
az deployment group create --resource-group varve-handover \
  --template-file deploy/azure/container-apps-handover/main.bicep \
  --parameters audience=api://varve operators='["Varve.Operator"]' \
               webhookObjectId={objectId} webhookClientId={appId} webhookIdentifierUri=api://varve-handover
```

`tenantId` defaults to the subscription's, and `authority` to its v2.0
issuer. Then declare the datasets with `varve apply` against the `url`
output, each with `placedOn` one instance and `standbyOn` the others it may
move to (ADR 0180), as in `../container-apps`.

## What is recovered how

- **A crash is recovered by Container Apps' own restart**, in seconds. The
  restarted member finds the record naming itself and reclaims at once: 2.0
  to 3.2 s to the first write again locally, plus the platform's restart.
  No alert is needed for it; a takeover a late alert starts finds the
  trigger stale and changes nothing (ADR 0181 point 2, amended).
- **The alerts are for what a restart does not recover:**
  - **a replica that is gone** (a zone lost, a replica that cannot start):
    the metric alert, at one-minute grain, is the fast one;
  - **a replica that runs but stays not ready**: the log search alert is a
    backstop behind the system logs' ingestion, typically 3–10 minutes end
    to end.
- **#164 measures both detection times** on real Container Apps, from the
  failure to the job's start. No figure is claimed here until then.
- The log search alert's query reads rows with `Reason_s` `Unhealthy`
  whose `Log_s` names the readiness probe. Microsoft's troubleshooting pages
  name `Unhealthy` rows as the probe failures in `ContainerAppSystemLogs_CL`;
  the message text that distinguishes a readiness failure is **to be
  confirmed on #164's leg**, as is the threshold (`readinessFailures`, 1 by
  default).

## The trust boundary

A job's start may **override its template**: whoever may start a job may run
a container of their choosing under the job's identity
([Jobs, Permissions](https://learn.microsoft.com/azure/container-apps/jobs#permissions)).
So the Logic App's identity is, in effect, as strong as the takeover jobs'
identity, an operator. The custom role keeps it to these jobs; the **OAuth
policy is the boundary**: only a v2.0 token from the tenant, for your app
registration, held by the AzNS AAD Webhook, reaches the trigger at all.

**Each app's ingress is also reachable directly**, at its own FQDN (the
`appFqdns` output), not only through Front Door. Front Door Standard has no
Private Link to the origins, and this template does not restrict the apps'
ingress to Front Door. Varve's own authentication applies on either path.

## Cost

Pay-as-you-go, West Europe, in US dollars. Each figure was confirmed on
2026-10-28 against the Azure Retail Prices API, which backs the pricing
pages, and Front Door's base fee and the free grants also on the pricing
pages themselves:

| What | Price | Notes |
|---|---|---|
| **N always-on replicas** | vCPU $0.000004 per second idle, $0.000034 active; memory $0.000004 per GiB-second | At the default 1 vCPU and 2 GiB, an idle replica is about $31 a month; the first 180,000 vCPU-seconds and 360,000 GiB-seconds a month are free per subscription. The two-app layout keeps one writer warm and its followers scale to zero. |
| **Front Door Standard** | $35 a month base fee | Plus requests ($0.009 per 10,000 in Europe) and data transfer. |
| Metric alerts | $0.10 per time series a month | The first 10 free: N apps, N series. |
| Log search alert | $3.00 a month at one minute ($1.50 at five) | Plus $0.30 a month for each app's time series after the first. |
| System logs' ingestion | $2.99 per GB | After the first 5 GB a month. |
| Logic App | $0.000025 per built-in action | After the first 4,000 a month. |
| Secure webhook | $6 per million notifications | |
| Takeover job | about $0.0003 for a 30-second run | 0.25 vCPU and 0.5 GiB at the active rate, before the free grant. |

The chain's own cost is a few cents a month at most; the layout's standing
cost is the N replicas and Front Door's base fee (ADR 0181 point 5).

CI builds and lints the template on every run.
