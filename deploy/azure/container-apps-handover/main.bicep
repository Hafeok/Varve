// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Varve's handover layout on Azure Container Apps: N single-replica apps
// behind Front Door, each a member of the writer set, and the automation that
// turns a failed member into a takeover request (ADRs 0179–0181;
// docs/operator/fleet.md). One resource group:
//
//   az deployment group create --resource-group varve-handover --template-file main.bicep \
//     --parameters audience=api://varve operators='["Varve.Operator"]' \
//                  webhookObjectId={objectId} webhookClientId={appId} webhookIdentifierUri=api://varve-handover
//
// No key, SAS token or connection string is created or read, here or by the
// server (ADR 0137): the storage account refuses shared-key access, each app
// reaches the container as its system-assigned identity, the takeover jobs
// take their operator token from their managed identity, and the Logic App's
// trigger refuses SAS and accepts only the action group's Entra token. The one
// key is the environment's own Log Analytics key, as in ../container-apps.
//
// N apps, one fleet (ADR 0181 point 5):
// - {name}-{instance}, exactly one replica each, external ingress: each is
//   one instance of the writer set, Varve:Fleet:Writer listing every member,
//   with Varve:Fleet:AutomaticHandover true. Datasets name their writer and
//   standbys among them with placedOn and standbyOn (ADR 0180).
// - Front Door Standard spreads clients over the members by /health/ready;
//   writes are forwarded between members on their own addresses (ADR 0168).
//
// The chain (ADR 0181 point 7 and its amendment of 2026-10-28):
// - a crash is Container Apps' to restart, and the restarted member reclaims;
// - a replica that is gone fires that app's metric alert, and one stuck not
//   ready fires the log search alert, both into one action group;
// - the action group calls the Logic App by Secure webhook, and the Logic App
//   starts the job named for the alerting app, {name}-{instance}-takeover;
// - the job runs `varve manage takeover --from {instance}` over the other
//   members, as a user-assigned identity the template makes an operator.
//
// The Secure webhook's Entra app registration is an operator-supplied
// prerequisite, passed by id (README.md). Datasets and grants are declared
// with `varve apply` (ADR 0163), as in ../container-apps.

@description('Where everything is deployed.')
param location string = resourceGroup().location

@description('A short name the resources are named from.')
@minLength(3)
@maxLength(16)
param name string = 'varve'

@description('The instances, one app each, {name}-{instance}: the writer set, Varve:Fleet:Writer, in order of preference. Lowercase letters and digits, at most 6 characters, so that {name}-{instance}-takeover fits a job\'s 32.')
@minLength(2)
param instances array = [
  'a'
  'b'
  'c'
]

@description('The server image, by digest in production: ghcr.io/hafeok/varve@sha256:….')
param image string = 'ghcr.io/hafeok/varve:latest'

@description('The Entra tenant of the deployment, the operator\'s app registrations and the managed identities.')
param tenantId string = subscription().tenantId

@description('The OIDC issuer, Varve:Auth:Authority (ADR 0037): the tenant\'s v2.0 issuer, which a managed identity\'s token names when the API accepts v2.0 tokens.')
param authority string = '${az.environment().authentication.loginEndpoint}${tenantId}/v2.0'

@description('The token audience, Varve:Auth:Audiences:0.')
param audience string

@description('The management plane\'s audience, Varve:Auth:Management:Audience; the data plane refuses its tokens (ADR 0163 point 1). The takeover jobs ask for it.')
param managementAudience string = 'api://varve-manage'

@description('The bootstrap operators, Varve:Manage:BootstrapOperators: agent IRIs or role values (ADR 0163 point 7). The takeover jobs\' identity is added to them.')
@minLength(1)
param operators array

@description('The Secure webhook\'s app registration: its object id (the application object, not its service principal).')
param webhookObjectId string

@description('The Secure webhook\'s app registration: its application (client) id, the aud of the v2.0 token the action group presents.')
param webhookClientId string

@description('The Secure webhook\'s app registration: its application ID URI, which the action group asks a token for.')
param webhookIdentifierUri string

@description('Readiness-probe failures of one app in five minutes that fire the log search alert.')
@minValue(1)
param readinessFailures int = 1

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

// Storage Blob Data Contributor: read, write and delete blobs; nothing else.
var blobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var cachePath = '/var/cache/varve'

// Azure Monitor's AzNS AAD Webhook application, which presents the Secure
// webhook's token (ADR 0181 point 7). Fixed by Microsoft.
var actionGroupsAppId = '461e8683-5575-4561-ac7f-899cc907d62a'

// The Logic App's request trigger, and the Container Apps API its action calls.
var triggerName = 'alert'
var jobsApiVersion = '2026-07-01'

var appNames = [for instance in instances: '${name}-${instance}']

// The writer set, the same on every member, the preferred writer first.
var writerSet = join(instances, ',')

// The takeover jobs' agent IRI: the issuer, #, and the oid of their identity
// (ADR 0163 point 7; Varve:Auth:SubjectClaim is oid below), so that the jobs
// are operators without an app role assigned by hand.
var takeoverAgent = '${az.environment().authentication.loginEndpoint}${tenantId}/v2.0#${takeoverIdentity.properties.principalId}'

// The bootstrap operators, one variable each, and the takeover jobs' last.
var operatorVariables = concat(map(range(0, length(operators)), i => {
  name: 'VARVE__MANAGE__BOOTSTRAPOPERATORS__${i}'
  value: operators[i]
}), [
  {
    name: 'VARVE__MANAGE__BOOTSTRAPOPERATORS__${length(operators)}'
    value: takeoverAgent
  }
])

// The settings every member shares; the instance and its address differ.
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
    // Entra's stable subject; the takeover jobs' agent IRI names it.
    name: 'VARVE__AUTH__SUBJECTCLAIM'
    value: 'oid'
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
  {
    name: 'VARVE__FLEET__WRITER'
    value: writerSet
  }
  {
    name: 'VARVE__FLEET__AUTOMATICHANDOVER'
    value: 'true'
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
    // management plane behind or mismatched (ADRs 0113, 0163). Front Door and
    // the log search alert both read it.
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

// The system logs land in the workspace's ContainerAppSystemLogs_CL table,
// which the log search alert reads.
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

// One app per member, exactly one replica: a second would claim the next
// epoch and supersede the first (ADR 0136), and none would leave the member
// gone. A restart is the platform's, and the restarted member reclaims at once.
resource apps 'Microsoft.App/containerApps@2024-03-01' = [for (instance, i) in instances: {
  name: appNames[i]
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
        // Front Door's origin, and every other member's forwards.
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
              name: 'VARVE__OBJECTSTORAGE__INSTANCE'
              value: instance
            }
            {
              // The address this member records at claim, which the others
              // forward to and the takeover jobs ask (ADR 0168 point 6). An
              // app with external ingress has no .internal. name; its own
              // FQDN is reached from inside the environment without leaving it.
              name: 'VARVE__FLEET__INTERNALURL'
              value: 'https://${appNames[i]}.${environment.properties.defaultDomain}'
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
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}]

// Every member may become a writer, so each reads and writes blobs in the
// container, and nothing else.
resource contributors 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (instance, i) in instances: {
  scope: container
  name: guid(container.id, apps[i].id, blobDataContributor)
  properties: {
    principalId: apps[i].identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributor)
  }
}]

// Front Door Standard: one endpoint, the members as one origin group, health
// by /health/ready. Standard has no Private Link, so each app's own ingress
// stays reachable directly as well (README.md).
resource frontDoor 'Microsoft.Cdn/profiles@2024-02-01' = {
  name: '${name}-fd'
  location: 'global'
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = {
  parent: frontDoor
  name: '${name}-${uniqueString(resourceGroup().id)}'
  location: 'global'
  properties: {
    enabledState: 'Enabled'
  }
}

resource members 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: frontDoor
  name: 'members'
  properties: {
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
      additionalLatencyInMilliseconds: 50
    }
    healthProbeSettings: {
      probePath: '/health/ready'
      probeRequestType: 'GET'
      probeProtocol: 'Https'
      probeIntervalInSeconds: 30
    }
    sessionAffinityState: 'Disabled'
  }
}

resource origins 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = [for (instance, i) in instances: {
  parent: members
  name: instance
  properties: {
    hostName: apps[i].properties.configuration.ingress.fqdn
    originHostHeader: apps[i].properties.configuration.ingress.fqdn
    httpPort: 80
    httpsPort: 443
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
    enforceCertificateNameCheck: true
  }
}]

resource route 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'all'
  // A route needs an origin in its group to be created.
  dependsOn: [
    origins
  ]
  properties: {
    originGroup: {
      id: members.id
    }
    supportedProtocols: [
      'Https'
    ]
    patternsToMatch: [
      '/*'
    ]
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Disabled'
    linkToDefaultDomain: 'Enabled'
    enabledState: 'Enabled'
  }
}

// The takeover jobs' one identity: an operator by its agent IRI above, on
// the management audience; it holds no Azure role.
resource takeoverIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${name}-takeover'
  location: location
}

// One job per member, its arguments fixed here so that each run is auditable
// from its definition (ADR 0181, considered and rejected). Started by the
// Logic App with no input.
resource jobs 'Microsoft.App/jobs@2024-03-01' = [for (instance, i) in instances: {
  name: '${appNames[i]}-takeover'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${takeoverIdentity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      // The command waits five seconds on the failed member, then a request
      // per dataset; a retry would only repeat an idempotent act.
      replicaTimeout: 120
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
    }
    template: {
      containers: [
        {
          name: 'takeover'
          image: image
          // The image's entrypoint is varve; these replace its serve.
          args: concat([
            'manage'
            'takeover'
            '--from'
            instance
          ], flatten(map(filter(instances, other => other != instance), other => [
            '--server'
            'https://${name}-${other}.${environment.properties.defaultDomain}'
          ])), [
            '--managed-identity'
            '--scope'
            '${managementAudience}/.default'
          ])
          env: [
            {
              // Picks the user-assigned identity at IDENTITY_ENDPOINT.
              name: 'AZURE_CLIENT_ID'
              value: takeoverIdentity.properties.clientId
            }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
    }
  }
}]

// The Logic App, Consumption: an HTTP request trigger that refuses SAS and
// accepts only the action group's token, and one action that starts the
// alerting app's job under its system-assigned identity.
resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: '${name}-handover'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    state: 'Enabled'
    accessControl: {
      // any(): Bicep's type for 2019-05-01 lacks sasAuthenticationPolicy,
      // which Azure Logic Apps documents for this object.
      triggers: any({
        // The trigger's URL carries no signature, and a signed one is refused.
        sasAuthenticationPolicy: {
          state: 'Disabled'
        }
        // A token must match every claim: Entra's v2.0 issuer for the tenant,
        // the app registration's client id, and the AzNS AAD Webhook as its
        // client. No other caller passes; this is the trust boundary.
        openAuthenticationPolicies: {
          policies: {
            actionGroup: {
              type: 'AAD'
              claims: [
                {
                  name: 'iss'
                  value: '${az.environment().authentication.loginEndpoint}${tenantId}/v2.0'
                }
                {
                  name: 'aud'
                  value: webhookClientId
                }
                {
                  name: 'azp'
                  value: actionGroupsAppId
                }
              ]
            }
          }
        }
      })
    }
    parameters: {
      apps: {
        value: appNames
      }
    }
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      parameters: {
        apps: {
          type: 'Array'
          defaultValue: []
        }
      }
      triggers: {
        '${triggerName}': {
          type: 'Request'
          kind: 'Http'
          inputs: {
            schema: {}
          }
        }
      }
      actions: {
        // A log search alert names the app as its dimension.
        Dimension: {
          type: 'Query'
          runAfter: {}
          inputs: {
            from: '@coalesce(triggerBody()?[\'data\']?[\'alertContext\']?[\'condition\']?[\'allOf\']?[0]?[\'dimensions\'], json(\'[]\'))'
            where: '@equals(item()?[\'name\'], \'ContainerAppName_s\')'
          }
        }
        // A metric alert names the app as its target, the last segment of its
        // id. Each operand has a default, so neither fails on the other's payload.
        App: {
          type: 'Compose'
          runAfter: {
            Dimension: [
              'Succeeded'
            ]
          }
          inputs: '@toLower(coalesce(first(union(body(\'Dimension\'), createArray(json(\'{}\'))))?[\'value\'], last(split(first(union(coalesce(triggerBody()?[\'data\']?[\'essentials\']?[\'alertTargetIDs\'], createArray()), createArray(\'\'))), \'/\'))))'
        }
        // Only a firing alert, and only for one of this template's apps; a
        // resolved alert, or any other, does nothing.
        Fired: {
          type: 'If'
          runAfter: {
            App: [
              'Succeeded'
            ]
          }
          expression: {
            and: [
              {
                equals: [
                  '@triggerBody()?[\'data\']?[\'essentials\']?[\'monitorCondition\']'
                  'Fired'
                ]
              }
              {
                contains: [
                  '@parameters(\'apps\')'
                  '@outputs(\'App\')'
                ]
              }
            ]
          }
          actions: {
            // Nothing is passed: the job's arguments are its definition's.
            Start: {
              type: 'Http'
              runAfter: {}
              inputs: {
                method: 'POST'
                uri: '${az.environment().resourceManager}subscriptions/${subscription().subscriptionId}/resourceGroups/${resourceGroup().name}/providers/Microsoft.App/jobs/@{outputs(\'App\')}-takeover/start?api-version=${jobsApiVersion}'
                authentication: {
                  type: 'ManagedServiceIdentity'
                  audience: az.environment().resourceManager
                }
              }
            }
          }
          else: {
            actions: {}
          }
        }
      }
      outputs: {}
    }
  }
}

// The Logic App's identity may read and start the takeover jobs and read their
// executions, nothing more: the built-in Container Apps Jobs Operator would
// also grant listSecrets (ADR 0181 point 7).
resource jobStarter 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(resourceGroup().id, 'varve-takeover-job-starter')
  properties: {
    roleName: 'Varve takeover job starter (${uniqueString(resourceGroup().id)})'
    description: 'Reads and starts the Varve handover layout\'s takeover jobs, and reads their executions.'
    type: 'CustomRole'
    assignableScopes: [
      resourceGroup().id
    ]
    permissions: [
      {
        actions: [
          'Microsoft.App/jobs/read'
          'Microsoft.App/jobs/start/action'
          'Microsoft.App/jobs/executions/read'
        ]
        notActions: []
      }
    ]
  }
}

// On each job, not the resource group. A start may override the job's
// template, so this identity is in effect as strong as the jobs' own.
resource jobStarters 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (instance, i) in instances: {
  scope: jobs[i]
  name: guid(jobs[i].id, logicApp.id, jobStarter.id)
  properties: {
    principalId: logicApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: jobStarter.id
  }
}]

// The one action group both alerts use. Its only receiver is a Secure webhook
// to the Logic App's trigger, with Entra authentication; never the "Logic
// Apps" receiver, which calls the trigger's SAS callback URL.
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${name}-handover'
  location: 'global'
  properties: {
    groupShortName: 'handover'
    enabled: true
    webhookReceivers: [
      {
        name: 'logic-app'
        // The trigger's URL without a signature: its access endpoint and path.
        serviceUri: '${logicApp.properties.accessEndpoint}/triggers/${triggerName}/paths/invoke?api-version=2016-10-01'
        useCommonAlertSchema: true
        useAadAuth: true
        objectId: webhookObjectId
        identifierUri: webhookIdentifierUri
        tenantId: tenantId
      }
    ]
  }
}

// A replica that is gone: a zone lost, a replica that cannot start. Replicas
// below 1, the minimum over a minute, evaluated every minute.
resource replicasGone 'Microsoft.Insights/metricAlerts@2018-03-01' = [for (instance, i) in instances: {
  name: '${appNames[i]}-replica-gone'
  location: 'global'
  properties: {
    description: '${appNames[i]} has no replica: start ${appNames[i]}-takeover (ADR 0181).'
    severity: 1
    enabled: true
    scopes: [
      apps[i].id
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT1M'
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'replicas'
          metricNamespace: 'Microsoft.App/containerApps'
          metricName: 'Replicas'
          operator: 'LessThan'
          threshold: 1
          timeAggregation: 'Minimum'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}]

// A replica running but stuck not ready: readiness-probe failures per app in
// the environment's system logs over five minutes, evaluated every minute, the
// app as the dimension. A backstop behind the logs' ingestion delay.
resource notReady 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: '${name}-not-ready'
  location: location
  kind: 'LogAlert'
  properties: {
    displayName: '${name}: a member stuck not ready'
    description: 'A member\'s readiness probe keeps failing: start its takeover job (ADR 0181).'
    severity: 1
    enabled: true
    scopes: [
      logs.id
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    // The table appears with the environment's first system log, after this
    // rule is created.
    skipQueryValidation: true
    autoMitigate: true
    criteria: {
      allOf: [
        {
          // A probe failure is a system log row with reason Unhealthy; its
          // message names the probe. To be confirmed on #164's leg.
          query: 'ContainerAppSystemLogs_CL\n| where Reason_s == "Unhealthy" and Log_s contains "readiness"\n| project TimeGenerated, ContainerAppName_s'
          timeAggregation: 'Count'
          operator: 'GreaterThanOrEqual'
          threshold: readinessFailures
          dimensions: [
            {
              name: 'ContainerAppName_s'
              operator: 'Include'
              values: appNames
            }
          ]
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

@description('The server\'s address: Front Door\'s endpoint.')
output url string = 'https://${endpoint.properties.hostName}'

@description('Each member\'s own FQDN, in the order of instances: Front Door\'s origins, also reachable directly.')
output appFqdns array = [for (instance, i) in instances: apps[i].properties.configuration.ingress.fqdn]

@description('The container the datasets are in.')
output containerUrl string = '${storage.properties.primaryEndpoints.blob}${container.name}'

@description('The takeover jobs\' identity, an operator by its agent IRI.')
output takeoverPrincipalId string = takeoverIdentity.properties.principalId

@description('The Logic App\'s identity, which may only read and start the takeover jobs.')
output logicAppPrincipalId string = logicApp.identity.principalId
