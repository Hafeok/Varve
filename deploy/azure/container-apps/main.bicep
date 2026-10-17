// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Varve on Azure Container Apps with its datasets in Blob Storage (ADRs
// 0134–0139, 0142; docs/operator/azure-container-apps.md). One resource group:
//
//   az deployment group create --resource-group varve --template-file main.bicep \
//     --parameters authority=https://login.microsoftonline.com/{tenant}/v2.0 audience=api://varve
//
// No key, SAS token or connection string is created or read, here or by the
// server (ADR 0137): the storage account refuses shared-key access, and the
// app reaches its container as its system-assigned managed identity, granted
// Storage Blob Data Contributor on that container alone.
//
// One replica at most. Opening a dataset for write claims the next writer
// epoch (ADR 0136): a second replica would supersede the first, whose writes
// then fail with writer-superseded. A revision rollover is a clean handover
// for the same reason: the new revision claims, the old one is superseded,
// and its readiness turns false before it is stopped.

@description('Where everything is deployed.')
param location string = resourceGroup().location

@description('A short name the resources are named from.')
@minLength(3)
@maxLength(16)
param name string = 'varve'

@description('The server image, by digest in production: ghcr.io/hafeok/varve@sha256:….')
param image string = 'ghcr.io/hafeok/varve:latest'

@description('The OIDC issuer, Varve:Auth:Authority (ADR 0037).')
param authority string

@description('The token audience, Varve:Auth:Audiences:0.')
param audience string

@description('The datasets this server serves, each in the container under its own prefix.')
param datasets array = [
  'default'
]

@description('The storage account\'s redundancy. Standard_ZRS keeps a commit through the loss of a zone; Standard_LRS is a cheaper store in one datacentre.')
@allowed([
  'Standard_ZRS'
  'Standard_LRS'
  'Standard_GZRS'
])
param storageSku string = 'Standard_ZRS'

@description('The most the extent page cache holds, Varve:ObjectStorage:CacheBytes (ADR 0139); within the replica\'s ephemeral storage.')
param cacheBytes int = 4294967296

@description('CPU cores for the replica.')
param cpu string = '1.0'

@description('Memory for the replica.')
param memory string = '2Gi'

@description('0 scales to zero between requests: a cold start reads the catalog and the log after its cursor (ADR 0139). 1 keeps one replica warm, so no request waits for a start.')
@allowed([
  0
  1
])
param minReplicas int = 0

// Storage Blob Data Contributor: read, write and delete blobs; nothing else.
var blobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var cachePath = '/var/cache/varve'

// Each dataset in the object store, under the prefix of its name.
var datasetVariables = [for dataset in datasets: {
  name: 'VARVE__DATASETS__${dataset}__STORAGE'
  value: 'ObjectStorage'
}]

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${name}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take('${toLower(replace(name, '-', ''))}${uniqueString(resourceGroup().id)}', 24)
  location: location
  kind: 'StorageV2'
  sku: {
    name: storageSku
  }
  properties: {
    // Identity only (ADR 0137): no account key, SAS or connection string works.
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    defaultToOAuthAuthentication: true
    accessTier: 'Hot'
  }
}

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobs
  name: 'datasets'
  properties: {
    publicAccess: 'None'
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${name}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      // One revision at a time: a new one claims the epoch, the old one is superseded.
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'varve'
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: concat([
            {
              name: 'VARVE__AUTH__MODE'
              value: 'Oidc'
            }
            {
              name: 'VARVE__AUTH__AUTHORITY'
              value: authority
            }
            {
              name: 'VARVE__AUTH__AUDIENCES__0'
              value: audience
            }
            {
              name: 'VARVE__AUTH__PRODUCTION'
              value: 'true'
            }
            {
              name: 'VARVE__OBJECTSTORAGE__PROVIDER'
              value: 'Azure'
            }
            {
              name: 'VARVE__OBJECTSTORAGE__CONTAINERURL'
              value: '${storage.properties.primaryEndpoints.blob}${container.name}'
            }
            {
              name: 'VARVE__OBJECTSTORAGE__CACHEPATH'
              value: cachePath
            }
            {
              name: 'VARVE__OBJECTSTORAGE__CACHEBYTES'
              value: string(cacheBytes)
            }
          ], datasetVariables)
          volumeMounts: [
            {
              volumeName: 'cache'
              mountPath: cachePath
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              periodSeconds: 10
            }
            {
              // Not ready while opening, behind, failed, superseded or draining (ADR 0113).
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              periodSeconds: 5
              failureThreshold: 3
            }
            {
              // Opening reads the catalog and replays the log after it.
              type: 'Startup'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              periodSeconds: 5
              failureThreshold: 60
            }
          ]
        }
      ]
      volumes: [
        {
          // The page cache and the local derived data: rebuilt by any replica.
          name: 'cache'
          storageType: 'EmptyDir'
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: 1
      }
    }
  }
}

// The app's identity may read and write blobs in its container, and nothing else.
resource dataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: container
  name: guid(container.id, app.id, blobDataContributor)
  properties: {
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributor)
  }
}

@description('The server\'s address.')
output url string = 'https://${app.properties.configuration.ingress.fqdn}'

@description('The container the datasets are in.')
output containerUrl string = '${storage.properties.primaryEndpoints.blob}${container.name}'

@description('The app\'s managed identity, for a role assignment beyond this template.')
output principalId string = app.identity.principalId
