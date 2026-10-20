# Varve on Azure Container Apps

`main.bicep` deploys a Varve fleet with its datasets in Blob Storage
(ADRs 0142, 0163, 0168; [Run on Azure Container Apps](../../../docs/operator/azure-container-apps.md),
[The fleet](../../../docs/operator/fleet.md)):

- a storage account that **refuses shared-key access**, and a private
  container, `datasets`, each dataset under its own prefix and the fleet's
  system dataset under `_fleet/`;
- a Container Apps environment and **two apps** of the image of ADR 0111,
  each with a system-assigned identity, `/health/live` and `/health/ready` as
  its probes, and an `EmptyDir` cache:
  - **`{name}-writer`**, internal ingress, **at most one replica**: the system
    writer, `Varve:Fleet:Writer` true, recording its internal address as
    `Varve:Fleet:InternalUrl`;
  - **`{name}`**, the public app, up to `maxReplicas`: each replica follows
    the datasets and forwards every write to the writer;
- **Storage Blob Data Contributor** on the container alone: for the writer
  unconditioned; for the public app with an ABAC condition that allows
  writes and deletes only under a `readers/` prefix, where a reader announces
  the catalog version it holds (#110).

```sh
az deployment group create --resource-group varve --template-file deploy/azure/container-apps/main.bicep \
  --parameters authority=https://login.microsoftonline.com/{tenant}/v2.0 audience=api://varve \
               operators='["Varve.Operator"]'
```

Datasets and grants are not template parameters: declare them with `varve
apply` (ADR 0163), as the deployer of `../entra/main.bicep` by workload
identity federation:

```sh
varve apply datasets.trig --server https://{url} \
  --authority https://login.microsoftonline.com/{tenant}/v2.0 --client-id {deployerClientId} \
  --client-assertion github --scope api://varve-manage/.default
```

No key, SAS token or connection string is created or read (ADR 0137). CI
builds and lints the template on every run; the what-if and a deployment run
in the optional `azure-objects` workflow.
