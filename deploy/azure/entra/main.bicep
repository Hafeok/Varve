// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The two Entra registrations of the on-behalf-of chain (ADR 0127,
// docs/operator/authenticate.md): Varve's API, with its app roles and the
// delegated scope a calling application exchanges a user's token for, and one
// calling application granted that scope with admin consent. Deployed at
// tenant scope with the Microsoft Graph Bicep extension:
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

@description('The calling application\'s own application ID URI: the audience of the tokens its users send it.')
param callerIdentifierUri string = 'api://varve-caller'

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
      description: 'Administer datasets: status, settings, checkpoints.'
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
