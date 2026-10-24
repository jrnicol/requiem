using 'main.bicep'

// Region chosen 2026-10-24. Resource-group location must match (DEPLOY.md creates it there).
param location = 'westus2'
param appName = 'requiem'
// Free tier chosen 2026-10-24. Change to 'B1' (about US$13/month) for always-on.
param appServiceSku = 'F1'
param githubRepository = 'jrnicol/requiem'
param githubEnvironment = 'production'

// Supplied on the command line at deploy time (DEPLOY.md), never committed:
//   sqlAdminObjectId, sqlAdminLogin, setupCode
param sqlAdminObjectId = readEnvironmentVariable('REQUIEM_SQL_ADMIN_OBJECT_ID', '')
param sqlAdminLogin = readEnvironmentVariable('REQUIEM_SQL_ADMIN_LOGIN', '')
param setupCode = readEnvironmentVariable('REQUIEM_SETUP_CODE', '')
