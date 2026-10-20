// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Varve on Azure Container Apps with its datasets in Blob Storage (ADRs
// 0134–0139, 0142, 0163, 0168; docs/operator/azure-container-apps.md and
// docs/operator/fleet.md). One resource group:
//
//   az deployment group create --resource-group varve --template-file main.bicep \
//     --parameters authority=https://login.microsoftonline.com/{tenant}/v2.0 audience=api://varve \
//                  operators='["Varve.Operator"]'
//
// No key, SAS token or connection string is created or read, here or by the
// server (ADR 0137): the storage account refuses shared-key access, and each
// app reaches the container as its system-assigned managed identity.
//
// Two apps, one fleet (ADR 0168 point 9):
// - {name}-writer, one replica at most, internal ingress: the system writer
//   and the writer of every dataset placed on it. Opening for write claims
//   the next writer epoch (ADR 0136), so a second replica would supersede
//   the first; a revision rollover is a clean handover for the same reason.
// - {name}, the public app, as many replicas as it scales to: each follows
//   the datasets (ADR 0164) and forwards every write to the writer's internal
//   address, which the writer record names.
//
// Datasets and grants are not here: they are declared with `varve apply`
// (ADR 0163), authenticated by workload identity federation.

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

@description('The management plane\'s audience, Varve:Auth:Management:Audience; the data plane refuses its tokens (ADR 0163 point 1).')
param managementAudience string = 'api://varve-manage'

@description('The bootstrap operators, Varve:Manage:BootstrapOperators: agent IRIs or role values (ADR 0163 point 7).')
@minLength(1)
param operators array

@description('The storage account\'s redundancy. Standard_ZRS keeps a commit through the loss of a zone; Standard_LRS is a cheaper store in one datacentre.')
@allowed([
  'Standard_ZRS'
  'Standard_LRS'
  'Standard_GZRS'
])
param storageSku string = 'Standard_ZRS'

@description('The most the extent page cache holds, Varve:ObjectStorage:CacheBytes (ADR 0139); within the replica\'s ephemeral storage.')
param cacheBytes int = 4294967296

@description('CPU cores for each replica.')
param cpu string = '1.0'

@description('Memory for each replica.')
param memory string = '2Gi'

@description('The writer: 0 scales it to zero between writes, and a write then waits for a start (ADR 0139); 1 keeps it warm.')
@allowed([
  0
  1
])
param writerMinReplicas int = 1

@description('The public app\'s fewest replicas; 0 scales it to zero between requests.')
param minReplicas int = 0

@description('The public app\'s most replicas: each follows the datasets and forwards writes.')
@minValue(1)
param maxReplicas int = 3

// Storage Blob Data Contributor: read, write and delete blobs; nothing else.
var blobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var cachePath = '/var/cache/varve'
var writerName = '${name}-writer'

// The writer's internal address, which it records at claim and the public
// app forwards to (ADR 0168 point 6): HTTPS inside the environment.
var writerAddress = 'https://${writerName}.internal.${environment.properties.defaultDomain}'

// The bootstrap operators, one variable each.
var operatorVariables = [for (operator, i) in operators: {
  name: 'VARVE__MANAGE__BOOTSTRAPOPERATORS__${i}'
  value: operator
}]

// The settings both apps share; the fleet keys differ.
var shared = concat([
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
    name: 'VARVE__AUTH__MANAGEMENT__AUDIENCE'
    value: managementAudience
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
], operatorVariables)

var probes = [
  {
    type: 'Liveness'
    httpGet: {
      path: '/health/live'
      port: 8080
    }
    periodSeconds: 10
  }
  {
    // Not ready while opening, behind, failed, superseded, draining, or the
    // management plane behind or mismatched (ADRs 0113, 0163).
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

resource writer 'Microsoft.App/containerApps@2024-03-01' = {
  name: writerName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      // One revision at a time: a new one claims the epochs, the old one is superseded.
      activeRevisionsMode: 'Single'
      ingress: {
        // Reached only by the public app's forwards, inside the environment.
        external: false
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
          env: concat(shared, [
            {
              name: 'VARVE__OBJECTSTORAGE__INSTANCE'
              value: writerName
            }
            {
              name: 'VARVE__FLEET__WRITER'
              value: 'true'
            }
            {
              name: 'VARVE__FLEET__INTERNALURL'
              value: writerAddress
            }
          ])
          volumeMounts: [
            {
              volumeName: 'cache'
              mountPath: cachePath
            }
          ]
          probes: probes
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
        minReplicas: writerMinReplicas
        maxReplicas: 1
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
          env: concat(shared, [
            {
              // Never claims: it follows, and forwards writes to the writer.
              name: 'VARVE__FLEET__WRITER'
              value: 'false'
            }
          ])
          volumeMounts: [
            {
              volumeName: 'cache'
              mountPath: cachePath
            }
          ]
          probes: probes
        }
      ]
      volumes: [
        {
          name: 'cache'
          storageType: 'EmptyDir'
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

// The writer's identity reads and writes blobs in the container, and nothing else.
resource writerContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: container
  name: guid(container.id, writer.id, blobDataContributor)
  properties: {
    principalId: writer.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributor)
  }
}

// The public app's identity reads every blob in the container, and writes
// or deletes only under a dataset's readers/ prefix, where a reader announces
// the catalog version it holds (#110's reader contract). An ABAC condition on
// the role assignment states it.
resource appContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: container
  name: guid(container.id, app.id, blobDataContributor)
  properties: {
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributor)
    conditionVersion: '2.0'
    condition: '((!(ActionMatches{\'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write\'}) AND !(ActionMatches{\'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/add/action\'}) AND !(ActionMatches{\'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/delete\'})) OR (@Resource[Microsoft.Storage/storageAccounts/blobServices/containers/blobs:path] StringLike \'*readers/*\'))'
  }
}

@description('The server\'s address: the public app.')
output url string = 'https://${app.properties.configuration.ingress.fqdn}'

@description('The writer\'s internal address, which its writer records name.')
output writerUrl string = writerAddress

@description('The container the datasets are in.')
output containerUrl string = '${storage.properties.primaryEndpoints.blob}${container.name}'

@description('The public app\'s managed identity, for a role assignment beyond this template.')
output principalId string = app.identity.principalId

@description('The writer\'s managed identity.')
output writerPrincipalId string = writer.identity.principalId
