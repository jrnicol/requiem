// Requiem on Azure (PLAN.md §6, PHASE2.md §2). Deploy into an empty resource group:
//   az deployment group create -g <rg> -f infra/main.bicep -p infra/main.bicepparam
// See DEPLOY.md for the whole first-time setup.

targetScope = 'resourceGroup'

@description('Azure region for every resource except the database.')
param location string = resourceGroup().location

@description('Azure region for the SQL server and database. Busy regions sometimes refuse new SQL servers for a subscription (RegionDoesNotAllowProvisioning); a neighbouring region works fine.')
param sqlLocation string = location

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

@description('Container image to run. GitHub Actions deploys the real one (ghcr.io/jrnicol/requiem); until then a Microsoft sample image holds the place. Rerunning this template resets it to this value, so run the workflow afterwards.')
param containerImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@description('Smallest replicas when idle. 0 scales to zero (free allowance lasts longer; ~10-20 s cold start); 1 keeps it warm (costs a few dollars a month).')
@minValue(0)
@maxValue(1)
param minReplicas int = 0

// Globally unique names derived from the resource group, so redeploys reuse the same resources.
var suffix = take(uniqueString(resourceGroup().id), 6)
var containerAppName = 'ca-${appName}-${suffix}'
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
  location: sqlLocation
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

  // Lets Container Apps reach the server (no private networking, to stay on the free allowance).
  resource allowAzure 'firewallRules' = {
    name: 'AllowAllWindowsAzureIps'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }

  resource database 'databases' = {
    name: databaseName
    location: sqlLocation
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

// ---------- App (Azure Container Apps) ----------
// Container Apps needs no App Service VM quota (Free Trial subscriptions have none) and has a monthly free
// allowance. One replica at most, because the app applies migrations on start (decided 2026-10-24).

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${appName}-${suffix}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${appIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080 // the .NET container images listen here
        transport: 'auto'
        allowInsecure: false
      }
      secrets: [
        {
          name: 'setup-code'
          keyVaultUrl: vault::setupCodeSecret.properties.secretUri
          identity: appIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: containerImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            // Container Apps ends TLS in front of the app; this makes the app see the original https request.
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'AZURE_CLIENT_ID', value: appIdentity.properties.clientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
            { name: 'Database__Provider', value: 'SqlServer' }
            { name: 'Database__MigrateOnStartup', value: 'true' }
            {
              name: 'ConnectionStrings__Requiem'
              value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${appIdentity.properties.clientId};Encrypt=True;Connect Timeout=60'
            }
            { name: 'Auth__SetupCode', secretRef: 'setup-code' }
          ]
          probes: [
            {
              // Generous: a paused database can take a minute to wake while the app migrates on start.
              type: 'Startup'
              httpGet: { path: '/healthz', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 18
            }
            {
              type: 'Liveness'
              httpGet: { path: '/healthz', port: 8080 }
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: 1
      }
    }
  }
  dependsOn: [appReadsSecrets]
}

// Contributor on the container app only: GitHub Actions can roll out new images but touch nothing else.
resource deployerCanDeploy 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(app.id, deployIdentity.id, 'b24988ac-6180-42a0-ab88-20f7382dd24c')
  scope: app
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Outputs (DEPLOY.md uses these) ----------

output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
output sqlServerName string = sqlServer.name
output databaseName string = databaseName
output appIdentityName string = appIdentity.name
output keyVaultName string = vault.name
@description('GitHub repository variable AZURE_CONTAINERAPP_NAME.')
output containerAppName string = app.name
@description('GitHub repository variable AZURE_RESOURCE_GROUP.')
output resourceGroupName string = resourceGroup().name
@description('GitHub repository variable AZURE_CLIENT_ID.')
output deployClientId string = deployIdentity.properties.clientId
@description('GitHub repository variable AZURE_TENANT_ID.')
output tenantId string = tenant().tenantId
@description('GitHub repository variable AZURE_SUBSCRIPTION_ID.')
output subscriptionId string = subscription().subscriptionId
