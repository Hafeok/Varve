# Varve on Azure Container Apps

`main.bicep` deploys a Varve server with its datasets in Blob Storage
(ADR 0142; [Run on Azure Container Apps](../../../docs/operator/azure-container-apps.md)):

- a storage account that **refuses shared-key access**, and a private
  container, `datasets`, each dataset under its own prefix;
- a Container Apps environment and the app: the image of ADR 0111, a
  system-assigned identity, `/health/live` and `/health/ready` as its probes,
  an `EmptyDir` cache, **at most one replica**;
- **Storage Blob Data Contributor** for the app's identity on the container
  alone.

```sh
az deployment group create --resource-group varve --template-file deploy/azure/container-apps/main.bicep \
  --parameters authority=https://login.microsoftonline.com/{tenant}/v2.0 audience=api://varve
```

No key, SAS token or connection string is created or read (ADR 0137). CI
builds and lints the template on every run; the what-if and a deployment run
in the optional `azure-objects` workflow.
