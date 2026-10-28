// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Varve Blob backend test environment.
// Deploy at resource-group scope into a dedicated, disposable resource group.
// Teardown: delete the resource group.
//
//   az deployment group what-if --resource-group <rg> --template-file varve-blob-test.bicep --parameters principalId=<object id>
//   az deployment group create  --resource-group <rg> --template-file varve-blob-test.bicep --parameters principalId=<object id>

targetScope = 'resourceGroup'

@description('Entra object id of the identity that runs the tests (Emil, or a dedicated test principal).')
param principalId string

@description('Principal type of principalId.')
@allowed([
  'User'
  'ServicePrincipal'
  'Group'
])
param principalType string = 'User'

@description('Create the data-plane role assignment. Needs Owner or User Access Administrator on the scope; set false if an admin assigns the role separately.')
param assignRole bool = true

@description('Optional public IPv4 addresses allowed to reach the account. Empty means any network (auth is still Entra-only).')
param allowedIpAddresses array = []

@description('Days after which test blobs are deleted by lifecycle policy.')
@minValue(1)
param retentionDays int = 7

param location string = resourceGroup().location

@description('Storage account name: 3-24 lowercase letters and digits, globally unique.')
@minLength(3)
@maxLength(24)
param storageAccountName string = 'varvetest${uniqueString(resourceGroup().id)}'

param tags object = {
  project: 'varve'
  purpose: 'blob-backend-failure-suite'
  disposable: 'true'
}

// Storage Blob Data Contributor (built-in role).
var blobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

// For-expressions are not allowed inside a ternary, so build the ACL objects as variables.
var ipRules = [for ip in allowedIpAddresses: {
  value: ip
  action: 'Allow'
}]

var networkAcls = empty(allowedIpAddresses)
  ? {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  : {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
      ipRules: ipRules
    }

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    // Entra only: no account keys, no SAS signed with keys. Matches Varve's no-API-keys rule.
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    // Flat namespace on purpose: HNS changes lease and rename semantics, and the backend targets plain Blob.
    isHnsEnabled: false
    accessTier: 'Hot'
    publicNetworkAccess: 'Enabled'
    networkAcls: networkAcls
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    // Keep the account behaving like a plain object store: no versioning or soft delete
    // that would mask overwrite and delete behaviour under test.
    isVersioningEnabled: false
    deleteRetentionPolicy: {
      enabled: false
    }
    containerDeleteRetentionPolicy: {
      enabled: false
    }
  }
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'varve-test'
  properties: {
    publicAccess: 'None'
  }
}

resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'expire-test-blobs'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: [
                'blockBlob'
                'appendBlob'
              ]
              prefixMatch: [
                'varve-test/'
              ]
            }
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: retentionDays
                }
              }
            }
          }
        }
      ]
    }
  }
}

resource blobDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (assignRole) {
  name: guid(storage.id, principalId, blobDataContributorRoleId)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
    principalId: principalId
    principalType: principalType
  }
}

output storageAccountName string = storage.name
output blobEndpoint string = storage.properties.primaryEndpoints.blob
output containerName string = container.name
