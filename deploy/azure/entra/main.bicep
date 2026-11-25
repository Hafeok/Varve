// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Varve's Entra registrations (ADR 0127, docs/operator/authenticate.md):
// Varve's API, with its app roles and the delegated scope a calling
// application exchanges a user's token for; the management plane's API, with
// its operator role and a delegated scope of its own; one calling application
// granted Varve's scope with admin consent; the deployer `varve apply` runs
// as; the Web UI's single-page application and the `varve` command line's
// public client, which a person signs in through for both planes; and the
// client CI's provider leg runs as (ADR 0223). Deployed at tenant scope with
// the Microsoft Graph Bicep extension:
//
//   az deployment tenant create --location westeurope --template-file main.bicep
//
// No secret is created, here or anywhere: the calling application
// authenticates by a federated credential, GitHub Actions' OIDC token from
// one environment of one repository (the same OIDC-only rule as everything
// else, ADR 0037), and the on-behalf-of leg's user signs in interactively
// by device code through a public client that holds no credential at all.

extension microsoftGraphV1

targetScope = 'tenant'

@description('Varve\'s application ID URI: the audience of every token for it, Varve:Auth:Audiences.')
param varveIdentifierUri string = 'api://varve'

@description('The management plane\'s application ID URI: Varve:Auth:Management:Audience, which the data plane refuses (ADR 0163 point 1).')
param managementIdentifierUri string = 'api://varve-manage'

@description('The GitHub environment whose Actions may run `varve apply` as the deployer: restrict it to main.')
param githubDeployEnvironment string = 'varve-deploy'

@description('The calling application\'s own application ID URI: the audience of the tokens its users send it.')
param callerIdentifierUri string = 'api://varve-caller'

@description('The Web UI\'s redirect URIs: each origin\'s `/ui/` the console is served at, https, or http on localhost only (ADR 0201).')
param webUiRedirectUris array = [
  'http://localhost:8080/ui/'
]

@description('The GitHub environment CI\'s provider leg runs in (ADR 0223): only the main branch may use it.')
param githubProviderEnvironment string = 'auth-providers'

@description('A suffix that keeps the registrations\' unique names apart between environments.')
param environmentName string = 'default'

@description('The GitHub repository whose Actions may sign in as the calling application, owner/name.')
param githubRepository string = 'Hafeok/Varve'

@description('The GitHub environment those Actions run in: only the main branch may use it.')
param githubEnvironment string = 'entra-obo'

// Stable ids, so that a redeployment updates the same roles and scope.
var readRoleId = guid('varve', 'role', 'Varve.Read')
var writeRoleId = guid('varve', 'role', 'Varve.Write')
var adminRoleId = guid('varve', 'role', 'Varve.Admin')
var accessAsUserId = guid('varve', 'scope', 'access_as_user')
var callerAccessAsUserId = guid('varve-caller', 'scope', 'access_as_user')
var operatorRoleId = guid('varve-manage', 'role', 'Varve.Operator')
var manageAccessAsUserId = guid('varve-manage', 'scope', 'access_as_user')

resource varve 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'varve-api-${environmentName}'
  displayName: 'Varve'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    varveIdentifierUri
  ]
  api: {
    // v2 tokens: iss is https://login.microsoftonline.com/{tenant}/v2.0.
    requestedAccessTokenVersion: 2
    oauth2PermissionScopes: [
      {
        id: accessAsUserId
        value: 'access_as_user'
        type: 'Admin'
        isEnabled: true
        adminConsentDisplayName: 'Access Varve as the signed-in user'
        adminConsentDescription: 'Lets an application call Varve on behalf of the signed-in user; the user\'s own Varve roles decide what it may do, and the user is the agent of every commit.'
      }
    ]
  }
  appRoles: [
    {
      id: readRoleId
      value: 'Varve.Read'
      displayName: 'Varve readers'
      description: 'Read datasets: queries, Graph Store reads, the commits.'
      allowedMemberTypes: [
        'User'
        'Application'
      ]
      isEnabled: true
    }
    {
      id: writeRoleId
      value: 'Varve.Write'
      displayName: 'Varve writers'
      description: 'Write datasets: updates and Graph Store writes; reads too.'
      allowedMemberTypes: [
        'User'
        'Application'
      ]
      isEnabled: true
    }
    {
      id: adminRoleId
      value: 'Varve.Admin'
      displayName: 'Varve administrators'
      description: 'Administer datasets: the management plane\'s dataset resources, as a dataset admin grant names the role.'
      allowedMemberTypes: [
        'User'
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource varvePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: varve.appId
}

// The management plane, a registration of its own (ADR 0163 point 1): its
// tokens name its audience alone, so the data plane refuses them, and a
// data-plane token is refused here. Varve.Operator is a bootstrap operator
// role value, Varve:Manage:BootstrapOperators.
resource management 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'varve-manage-${environmentName}'
  displayName: 'Varve management'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    managementIdentifierUri
  ]
  api: {
    requestedAccessTokenVersion: 2
    // A person's own operator role, through the Web UI or the command line:
    // the user is the agent, and Varve.Operator in their token decides.
    oauth2PermissionScopes: [
      {
        id: manageAccessAsUserId
        value: 'access_as_user'
        type: 'Admin'
        isEnabled: true
        adminConsentDisplayName: 'Manage Varve as the signed-in user'
        adminConsentDescription: 'Lets the Web UI or the varve command line call Varve\'s management plane on behalf of the signed-in user; the user\'s own roles decide what it may do.'
      }
    ]
  }
  appRoles: [
    {
      id: operatorRoleId
      value: 'Varve.Operator'
      displayName: 'Varve operators'
      description: 'Manage the fleet: declare datasets, grants and placement; read the system dataset.'
      allowedMemberTypes: [
        'User'
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource managementPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: management.appId
}

// The deployer: what CI runs `varve apply` as, by workload identity
// federation (ADR 0163 point 5). Its only credential is GitHub's OIDC token
// for one environment of one repository; it holds Varve.Operator.
resource deployer 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'varve-deployer-${environmentName}'
  displayName: 'Varve deployer'
  signInAudience: 'AzureADMyOrg'
}

resource deployerPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: deployer.appId
}

resource deployerOperates 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: operatorRoleId
  principalId: deployerPrincipal.id
  resourceId: managementPrincipal.id
}

resource deployerFromGitHub 'Microsoft.Graph/applications/federatedIdentityCredentials@v1.0' = {
  name: '${deployer.uniqueName}/github-${githubDeployEnvironment}'
  audiences: [
    'api://AzureADTokenExchange'
  ]
  issuer: 'https://token.actions.githubusercontent.com'
  subject: 'repo:${githubRepository}:environment:${githubDeployEnvironment}'
  description: 'GitHub Actions in ${githubRepository}, environment ${githubDeployEnvironment}: varve apply.'
}

resource caller 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'varve-caller-${environmentName}'
  displayName: 'Varve caller (on behalf of its users)'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    callerIdentifierUri
  ]
  api: {
    requestedAccessTokenVersion: 2
    oauth2PermissionScopes: [
      {
        id: callerAccessAsUserId
        value: 'access_as_user'
        type: 'User'
        isEnabled: true
        adminConsentDisplayName: 'Use the application as the signed-in user'
        adminConsentDescription: 'The scope the application\'s own clients ask for.'
        userConsentDisplayName: 'Use the application as you'
        userConsentDescription: 'Lets the application act for you.'
      }
    ]
  }
  // Varve's access_as_user, delegated: what the exchange asks for.
  requiredResourceAccess: [
    {
      resourceAppId: varve.appId
      resourceAccess: [
        {
          id: accessAsUserId
          type: 'Scope'
        }
      ]
    }
  ]
}

resource callerPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: caller.appId
}

// Admin consent for the whole tenant, so no user is prompted for Varve's scope.
resource consent 'Microsoft.Graph/oauth2PermissionGrants@v1.0' = {
  clientId: callerPrincipal.id
  resourceId: varvePrincipal.id
  consentType: 'AllPrincipals'
  scope: 'access_as_user'
}

// Varve.Write as an application permission: the calling application's own
// writes (its setup, an import) take its client-credentials token and name
// the application as their agent (the attribution rule, ADR 0127).
resource callerWrites 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: writeRoleId
  principalId: callerPrincipal.id
  resourceId: varvePrincipal.id
}

// The calling application's only credential: GitHub's OIDC token for one
// environment of one repository, exchanged for the application's own.
resource callerFromGitHub 'Microsoft.Graph/applications/federatedIdentityCredentials@v1.0' = {
  name: '${caller.uniqueName}/github-${githubEnvironment}'
  audiences: [
    'api://AzureADTokenExchange'
  ]
  issuer: 'https://token.actions.githubusercontent.com'
  subject: 'repo:${githubRepository}:environment:${githubEnvironment}'
  description: 'GitHub Actions in ${githubRepository}, environment ${githubEnvironment}: the on-behalf-of leg, run by hand.'
}

// A public client a person signs in through by device code, for a token to
// the calling application's API: the user token the on-behalf-of leg starts
// from. It holds no credential; the person's own sign-in, with their MFA, is
// the only one.
resource signIn 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'varve-caller-sign-in-${environmentName}'
  displayName: 'Varve caller sign-in (device code)'
  signInAudience: 'AzureADMyOrg'
  isFallbackPublicClient: true
  requiredResourceAccess: [
    {
      resourceAppId: caller.appId
      resourceAccess: [
        {
          id: callerAccessAsUserId
          type: 'Scope'
        }
      ]
    }
  ]
}

resource signInPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: signIn.appId
}

resource signInConsent 'Microsoft.Graph/oauth2PermissionGrants@v1.0' = {
  clientId: signInPrincipal.id
  resourceId: callerPrincipal.id
  consentType: 'AllPrincipals'
  scope: 'access_as_user'
}

// The Web UI (ADR 0201): a single-page application, so its token endpoint
// answers CORS; authorisation code with PKCE, no secret. It asks for each
// plane's access_as_user, one token per plane (ADR 0201 point 2).
resource webUi 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'varve-webui-${environmentName}'
  displayName: 'Varve Web UI'
  signInAudience: 'AzureADMyOrg'
  spa: {
    redirectUris: webUiRedirectUris
  }
  requiredResourceAccess: [
    {
      resourceAppId: varve.appId
      resourceAccess: [
        {
          id: accessAsUserId
          type: 'Scope'
        }
      ]
    }
    {
      resourceAppId: management.appId
      resourceAccess: [
        {
          id: manageAccessAsUserId
          type: 'Scope'
        }
      ]
    }
  ]
}

resource webUiPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: webUi.appId
}

resource webUiDataConsent 'Microsoft.Graph/oauth2PermissionGrants@v1.0' = {
  clientId: webUiPrincipal.id
  resourceId: varvePrincipal.id
  consentType: 'AllPrincipals'
  scope: 'access_as_user'
}

resource webUiManageConsent 'Microsoft.Graph/oauth2PermissionGrants@v1.0' = {
  clientId: webUiPrincipal.id
  resourceId: managementPrincipal.id
  consentType: 'AllPrincipals'
  scope: 'access_as_user'
}

// The `varve` command line (ADR 0105): a public client a person signs in
// through by device code, for either plane. No credential; the person's own
// sign-in, with their MFA, is the only one. Its refresh token is kept in the
// credential file unless --no-store.
resource cli 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'varve-cli-${environmentName}'
  displayName: 'Varve command line'
  signInAudience: 'AzureADMyOrg'
  isFallbackPublicClient: true
  requiredResourceAccess: [
    {
      resourceAppId: varve.appId
      resourceAccess: [
        {
          id: accessAsUserId
          type: 'Scope'
        }
      ]
    }
    {
      resourceAppId: management.appId
      resourceAccess: [
        {
          id: manageAccessAsUserId
          type: 'Scope'
        }
      ]
    }
  ]
}

resource cliPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: cli.appId
}

resource cliDataConsent 'Microsoft.Graph/oauth2PermissionGrants@v1.0' = {
  clientId: cliPrincipal.id
  resourceId: varvePrincipal.id
  consentType: 'AllPrincipals'
  scope: 'access_as_user'
}

resource cliManageConsent 'Microsoft.Graph/oauth2PermissionGrants@v1.0' = {
  clientId: cliPrincipal.id
  resourceId: managementPrincipal.id
  consentType: 'AllPrincipals'
  scope: 'access_as_user'
}

// CI's provider leg (ADR 0223): client credentials whose only credential is
// GitHub's OIDC token for the auth-providers environment, holding Varve.Write
// as an application permission, so its writes name the client as their agent.
resource providerLeg 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'varve-provider-leg-${environmentName}'
  displayName: 'Varve CI provider leg'
  signInAudience: 'AzureADMyOrg'
}

resource providerLegPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: providerLeg.appId
}

resource providerLegWrites 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: writeRoleId
  principalId: providerLegPrincipal.id
  resourceId: varvePrincipal.id
}

resource providerLegFromGitHub 'Microsoft.Graph/applications/federatedIdentityCredentials@v1.0' = {
  name: '${providerLeg.uniqueName}/github-${githubProviderEnvironment}'
  audiences: [
    'api://AzureADTokenExchange'
  ]
  issuer: 'https://token.actions.githubusercontent.com'
  subject: 'repo:${githubRepository}:environment:${githubProviderEnvironment}'
  description: 'GitHub Actions in ${githubRepository}, environment ${githubProviderEnvironment}: the Entra provider leg (ADR 0223).'
}

@description('The public client the on-behalf-of leg\'s user signs in through by device code.')
output signInClientId string = signIn.appId

@description('Varve:Auth:Authority is https://login.microsoftonline.com/{tenant}/v2.0; Varve:Auth:Audiences is this URI.')
output varveAudience string = varveIdentifierUri

@description('The calling application\'s client id, for its MSAL configuration.')
output callerClientId string = caller.appId

@description('The scope the calling application exchanges a user\'s token for.')
output exchangeScope string = '${varveIdentifierUri}/access_as_user'

@description('The Varve.Write role id, to assign to users and groups.')
output writeRoleId string = writeRoleId

@description('Varve:Auth:Management:Audience is this URI; `varve apply --scope` asks for {this}/.default.')
output managementAudience string = managementIdentifierUri

@description('The deployer\'s client id: `varve apply --client-id`, with `--client-assertion github`.')
output deployerClientId string = deployer.appId

@description('The Web UI\'s client id: Varve:WebUi:Auth:ClientId, or the bundle\'s Varve:Auth:ClientId.')
output webUiClientId string = webUi.appId

@description('The Web UI\'s and the command line\'s scope for the data plane.')
output dataScope string = '${varveIdentifierUri}/access_as_user'

@description('The Web UI\'s and the command line\'s scope for the management plane.')
output manageScope string = '${managementIdentifierUri}/access_as_user'

@description('The command line\'s client id: `varve … --authority … --client-id`, by device code.')
output cliClientId string = cli.appId

@description('VARVE_ENTRA_CLIENT_ID in the auth-providers environment (ADR 0223); VARVE_ENTRA_AUDIENCE is varveAudience.')
output providerLegClientId string = providerLeg.appId
