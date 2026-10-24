using 'main.bicep'

// Regions can be changed per deploy from Cloud Shell without editing this file:
//   export REQUIEM_LOCATION=westus3 REQUIEM_SQL_LOCATION=westus3
// West US 2 refused new SQL servers for this subscription on 2026-10-24, so the default is West US 3.
// The resource group's region doesn't have to match.
param location = readEnvironmentVariable('REQUIEM_LOCATION', 'westus3')
param sqlLocation = readEnvironmentVariable('REQUIEM_SQL_LOCATION', 'westus3')
param appName = 'requiem'
// As GitHub writes it in the deploy token's subject, with numeric owner and repository IDs (seen 2026-10-24).
param githubRepository = 'jrnicol@9485852/requiem@1431808700'
param githubEnvironment = 'production'

// Supplied from Cloud Shell at deploy time (DEPLOY.md), never committed.
param sqlAdminObjectId = readEnvironmentVariable('REQUIEM_SQL_ADMIN_OBJECT_ID', '')
param sqlAdminLogin = readEnvironmentVariable('REQUIEM_SQL_ADMIN_LOGIN', '')
param setupCode = readEnvironmentVariable('REQUIEM_SETUP_CODE', '')
