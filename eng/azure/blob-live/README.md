# A disposable storage account for the live Azure leg

`varve-blob-test.bicep` creates what the object-storage backend's live leg
(`docs/testing.md` §9, `docs/reports/azure-blob-live-2026-10-23.md`) runs
against: a precursor environment for developer-driven runs from a
workstation, not a deployment of Varve. Deploy it into a resource group of
its own and delete the group when done.

**What it creates.** One StorageV2 account, `Standard_LRS`, Hot tier, flat
namespace (no hierarchical namespace, which changes rename and lease
semantics the backend never uses), blob versioning and soft delete off so
that overwrites and deletes behave as a plain object store's, shared-key
access **disabled** and OAuth by default (ADR 0137: no key, no SAS), TLS
1.2, HTTPS only, public network access with an optional IP allow-list; the
container `varve-test`; a lifecycle rule that deletes blobs under
`varve-test/` after `retentionDays`; and, when `assignRole` is true, Storage
Blob Data Contributor on the account for `principalId`.

**Parameters.** `principalId` (required: the object id of whoever runs the
tests), `principalType` (`User` unless set), `assignRole` (`true`; set it
`false` and have an administrator assign the role when you lack Owner or
User Access Administrator on the group), `allowedIpAddresses` (empty: any
network), `retentionDays` (7), `location` (the group's), `storageAccountName`
(derived from the group unless set), `tags`.

**Identity is the operator's concern**, not the template's: who the principal
is, which tenant and subscription the CLI profile is signed in to
(`AZURE_CONFIG_DIR`), and the rights to assign the role. The template holds
no tenant, subscription or account identifier.

```bash
az deployment group what-if --resource-group <group> --template-file varve-blob-test.bicep --parameters principalId=<object id>
az deployment group create  --resource-group <group> --template-file varve-blob-test.bicep --parameters principalId=<object id>
```

`what-if` first, every time. Teardown is deleting the resource group. The
tests themselves need only the data plane; they are run as `docs/testing.md`
§9 says, with `VARVE_AZURE_BLOB_ENDPOINT` set to the `blobEndpoint` output.
