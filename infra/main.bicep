// Azure resources for one deployed copy of dashboard-starter:
//
//   App Service plan (Linux) -> Web App running the API, which also serves the React build
//   Azure SQL logical server  -> one database
//
// Run on purpose, like the migrate command. It creates what's missing and updates what
// changed; running it twice is safe. See docs/deploy-azure.md for the full walkthrough.
//
//   az deployment group create --resource-group <group> --template-file infra/main.bicep \
//     --parameters appName=<name> sqlAdminPassword=<password>
//
// What this file deliberately does NOT do:
//   - Create the schema. The deploy workflow runs `src/Db migrate` for that.
//   - Hold the API's connection string. The deploy workflow sets it from a GitHub secret.
//     App settings listed here would replace the whole set on every run of this file and
//     wipe the connection string, so the web app gets none here at all.

targetScope = 'resourceGroup'

@description('Base name for every resource, such as dashboard-starter-demo. The web app is <appName>.azurewebsites.net, so it must be unique across Azure.')
@minLength(3)
@maxLength(40)
param appName string

@description('Azure region. Defaults to the resource group\'s region.')
param location string = resourceGroup().location

@description('Admin login for the SQL server. Used only by the migrate and app-user commands, never by the API.')
param sqlAdminLogin string = 'dashboard_admin'

@description('Password for the SQL admin login. Store the same value in the SQL_ADMIN_PASSWORD GitHub secret.')
@secure()
param sqlAdminPassword string

@description('true: Azure SQL\'s free serverless database (pauses when idle, so the first request after a pause is slow). false: an always-on Basic database, a few dollars a month.')
param useFreeDatabase bool = true

@description('App Service plan size. B1 is the smallest Linux size with Always On.')
param appServiceSku string = 'B1'

var databaseName = 'DashboardStarter'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${appName}-plan'
  location: location
  kind: 'linux'
  sku: {
    name: appServiceSku
  }
  properties: {
    // Required for Linux plans.
    reserved: true
  }
}

resource web 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id
    // The App Service front end redirects http to https before a request reaches the app.
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      // Said out loud rather than left to auto-detection: the deployed folder also holds
      // DashboardStarter.Db.dll, which the API references for its list of migrations.
      appCommandLine: 'dotnet DashboardStarter.Api.dll'
      alwaysOn: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
    }
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: '${appName}-sql'
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Lets the web app reach the database. "Allow Azure services" is the 0.0.0.0 rule: it admits
// traffic from any Azure IP address, not only ours, so the login is what keeps others out.
// The private alternative (VNet integration plus a private endpoint) costs more and adds
// networking to learn; a reasonable later step for a real product.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: useFreeDatabase
    ? {
        // General Purpose, serverless, Gen5, up to 2 vCores: the free offer's shape.
        name: 'GP_S_Gen5_2'
        tier: 'GeneralPurpose'
      }
    : {
        name: 'Basic'
        tier: 'Basic'
      }
  properties: useFreeDatabase
    ? {
        // One free database per subscription. When the monthly allowance runs out, the
        // database pauses until next month instead of billing.
        useFreeLimit: true
        freeLimitExhaustionBehavior: 'AutoPause'
        autoPauseDelay: 60
      }
    : {}
}

output webAppName string = web.name
output webAppHostName string = web.properties.defaultHostName
output sqlServerName string = sqlServer.name
output sqlServerHostName string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
