// Requiem on Azure (PLAN.md §6, PHASE2.md §2). Deploy into an empty resource group:
//   az deployment group create -g <rg> -f infra/main.bicep -p infra/main.bicepparam
// See DEPLOY.md for the whole first-time setup.

targetScope = 'resourceGroup'

@description('Azure region for every resource.')
param location string = resourceGroup().location

@description('Short name used to build resource names.')
@minLength(3)
@maxLength(12)
param appName string = 'requiem'

@description('Object ID of the Microsoft Entra user who administers the database (you). Find it in the portal under Microsoft Entra ID > Users, or with: az ad signed-in-user show --query id -o tsv')
param sqlAdminObjectId string

@description('Sign-in name of that user, e.g. you@example.com.')
param sqlAdminLogin string

@description('One-time code that unlocks first-run setup (/setup) in the hosted app. Remove it from Key Vault after setup.')
@secure()
@minLength(12)
param setupCode string

@description('GitHub repository allowed to deploy, as owner/name.')
param githubRepository string = 'jrnicol/requiem'

@description('GitHub environment the deploy job runs in. GitHub names it in the sign-in token, so it must match .github/workflows/deploy.yml.')
param githubEnvironment string = 'production'

@description('App Service plan size. F1 is free (sleeps when idle, 60 CPU-minutes a day); B1 is the cheapest always-on tier.')
@allowed(['F1', 'B1', 'P0v3'])
param appServiceSku string = 'F1'

// Globally unique names derived from the resource group, so redeploys reuse the same resources.
var suffix = take(uniqueString(resourceGroup().id), 6)
var webAppName = 'app-${appName}-${suffix}'
var sqlServerName = 'sql-${appName}-${suffix}'
var keyVaultName = 'kv-${appName}-${suffix}'
var databaseName = appName

// ---------- Identities ----------

@description('The app\'s own identity: signs in to SQL and reads Key Vault, with no passwords anywhere.')
resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${appName}-app'
  location: location
}

@description('GitHub Actions signs in as this identity through OpenID Connect (no stored secrets) to deploy the app.')
resource deployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${appName}-deploy'
  location: location

  resource github 'federatedIdentityCredentials' = {
    name: 'github-${githubEnvironment}'
    properties: {
      issuer: 'https://token.actions.githubusercontent.com'
      subject: 'repo:${githubRepository}:environment:${githubEnvironment}'
      audiences: ['api://AzureADTokenExchange']
    }
  }
}

// ---------- Monitoring ----------

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${appName}-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    // Stays well inside the monthly free ingestion allowance.
    workspaceCapping: { dailyQuotaGb: json('0.15') }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${appName}-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------- Secrets ----------

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }

  resource setupCodeSecret 'secrets' = {
    name: 'Auth--SetupCode'
    properties: { value: setupCode }
  }
}

// Key Vault Secrets User: read secret values.
resource appReadsSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, appIdentity.id, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Database ----------

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    // Microsoft Entra sign-in only: there is no SQL password to leak.
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      principalType: 'User'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: tenant().tenantId
    }
  }

  // Lets App Service reach the server (the free tier has no private networking).
  resource allowAzure 'firewallRules' = {
    name: 'AllowAllWindowsAzureIps'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }

  resource database 'databases' = {
    name: databaseName
    location: location
    sku: {
      name: 'GP_S_Gen5'
      tier: 'GeneralPurpose'
      family: 'Gen5'
      capacity: 2
    }
    properties: {
      // Azure SQL free offer (one per subscription): pauses when the monthly allowance runs out instead of billing.
      useFreeLimit: true
      freeLimitExhaustionBehavior: 'AutoPause'
      autoPauseDelay: 60
      minCapacity: json('0.5')
      zoneRedundant: false
      requestedBackupStorageRedundancy: 'Local'
    }
  }
}

// ---------- App ----------

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${appName}-${suffix}'
  location: location
  kind: 'linux'
  sku: { name: appServiceSku }
  properties: { reserved: true }
}

resource web 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${appIdentity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: appIdentity.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: appServiceSku != 'F1' // not available on the free tier
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/healthz'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        // App Service ends TLS in front of the app; this makes the app see the original https request.
        { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
        { name: 'AZURE_CLIENT_ID', value: appIdentity.properties.clientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'Database__Provider', value: 'SqlServer' }
        { name: 'Database__MigrateOnStartup', value: 'true' }
        {
          name: 'ConnectionStrings__Requiem'
          value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${appIdentity.properties.clientId};Encrypt=True;Connect Timeout=60'
        }
        { name: 'Auth__SetupCode', value: '@Microsoft.KeyVault(SecretUri=${vault::setupCodeSecret.properties.secretUri})' }
      ]
    }
  }
  dependsOn: [appReadsSecrets]
}

// Website Contributor on the app only: GitHub Actions can deploy code but touch nothing else.
resource deployerCanDeploy 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(web.id, deployIdentity.id, 'de139f84-1756-47ae-9be6-808fbbe84772')
  scope: web
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'de139f84-1756-47ae-9be6-808fbbe84772')
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Outputs (DEPLOY.md uses these) ----------

output webAppName string = web.name
output webAppUrl string = 'https://${web.properties.defaultHostName}'
output sqlServerName string = sqlServer.name
output databaseName string = databaseName
output appIdentityName string = appIdentity.name
output keyVaultName string = vault.name
@description('GitHub repository variable AZURE_CLIENT_ID.')
output deployClientId string = deployIdentity.properties.clientId
@description('GitHub repository variable AZURE_TENANT_ID.')
output tenantId string = tenant().tenantId
@description('GitHub repository variable AZURE_SUBSCRIPTION_ID.')
output subscriptionId string = subscription().subscriptionId
