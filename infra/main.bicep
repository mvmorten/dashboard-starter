// Azure resources for one deployed copy of dashboard-starter:
//
//   App Service plan (Linux) -> Web App running the API, which also serves the React build
//   Azure SQL logical server  -> one database
//   Storage account + Key Vault -> the Data Protection keys behind the sign-in cookie
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
//   - Send email. Password reset emails go through Resend, an HTTP service outside Azure,
//     set up by hand (docs/deploy-azure.md, step 7). Nothing for it lives in this file.

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

@description('false (default): an always-on Basic database, a few dollars a month. true: Azure SQL\'s free serverless database, which pauses when idle, so the first request after a pause waits up to a minute.')
param useFreeDatabase bool = false

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
  // An identity Azure manages for the app, so it can reach the Data Protection key storage
  // below without any secret in its settings.
  identity: {
    type: 'SystemAssigned'
  }
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

// ---- Data Protection keys (step 5) ----
//
// ASP.NET Core encrypts the auth and antiforgery cookies with Data Protection keys. Kept in
// the container they'd be lost on every deploy, signing everyone out. Instead the key ring is
// one blob in this storage account, and each key in it is encrypted with an RSA key in this
// Key Vault that never leaves the vault (src/Api/Hosting/DataProtectionSetup.cs).
//
// The deploy workflow finds both by the purpose tag and gives the app their addresses as app
// settings. Names come from the resource group's id, so they're unique across Azure (both
// need to be) and stay the same each time this file runs.
var keyStoreSuffix = uniqueString(resourceGroup().id)

resource keyStorage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'dp${keyStoreSuffix}'
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  tags: {
    purpose: 'dataprotection'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    // Only signed-in identities with a role below get in: no anonymous blobs, no account keys.
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
  }
}

resource keyBlobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: keyStorage
  name: 'default'
}

resource keyContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: keyBlobs
  name: 'dataprotection'
  properties: {
    publicAccess: 'None'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv${keyStoreSuffix}'
  location: location
  tags: {
    purpose: 'dataprotection'
  }
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    // Access by Azure roles (below), not the older per-vault access policies.
    enableRbacAuthorization: true
    // Soft delete can't be turned off. Purge protection stays off, so tearing everything
    // down can purge the vault (docs/deploy-azure.md, "Tear it all down").
    softDeleteRetentionInDays: 7
  }
}

// Creates the first version if the key doesn't exist; a rerun leaves an existing key alone.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: keyVault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

// Storage Blob Data Contributor, on the one container: read and write the key ring.
resource keyContainerAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyContainer
  name: guid(keyContainer.id, web.id, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: web.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Key Vault Crypto Service Encryption User, on the vault: wrap and unwrap with its keys,
// nothing else (it can't read secrets, export keys, or delete anything).
resource keyVaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, web.id, 'e147488a-f6f5-4113-8e2d-b22465e65bf6')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'e147488a-f6f5-4113-8e2d-b22465e65bf6')
    principalId: web.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output webAppName string = web.name
output webAppHostName string = web.properties.defaultHostName
output sqlServerName string = sqlServer.name
output sqlServerHostName string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
output keyVaultName string = keyVault.name
output dataProtectionBlobUri string = '${keyStorage.properties.primaryEndpoints.blob}dataprotection/keys.xml'
output dataProtectionKeyId string = dataProtectionKey.properties.keyUriWithVersion
