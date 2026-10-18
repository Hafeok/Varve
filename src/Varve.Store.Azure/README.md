# Varve.Store.Azure

Azure Blob Storage for `Varve.Store.ObjectStorage` (ADR 0134): the five
object-store operations over the Blob service's REST API and `HttpClient`,
with no Azure SDK.

- **Conditional writes** by `If-None-Match: *` (create-if-absent) and
  `If-Match` (replace-if-unchanged), at a pinned `x-ms-version`.
- **The workload's identity, never a key** (ADR 0137). A token comes from the
  Container Apps managed-identity endpoint (`IDENTITY_ENDPOINT`,
  `IDENTITY_HEADER`), from workload identity (`AZURE_FEDERATED_TOKEN_FILE`,
  `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`), or on a workstation from
  `az account get-access-token`. Tokens are cached until shortly before they
  expire and never logged.
- **A development key only on loopback**: Azurite's well-known account, for
  a container URL whose host is the local machine.
- The principal needs **Storage Blob Data Contributor** on the container.

```csharp
using AzureBlobStore store = AzureBlobStore.Create(new AzureBlobStoreOptions
{
    ContainerUrl = new Uri("https://account.blob.core.windows.net/dataset"),
    Clock = TimeProvider.System,
});
```
