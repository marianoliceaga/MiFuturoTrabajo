// MiFuturoTrabajo - all infrastructure, in one deployment.
//
// Cost is the binding constraint: this is a portfolio demo, not a business, so
// every component below sits on a free tier or a consumption grant. The two
// exceptions are Blob Storage and Communication Services, which are pay-as-you-go
// and cost cents per month at demo volume.
//
//   az deployment group create -g <rg> -f infra/main.bicep -p infra/main.bicepparam
//
// Deliberately NOT here: the JWT signing key and the demo password. They are
// secrets, they would end up in deployment history, and they are set separately
// (see infra/README.md).

targetScope = 'resourceGroup'

@description('Short name used as a prefix for every resource. Lowercase letters and digits.')
@minLength(3)
@maxLength(20)
param appName string = 'mifuturotrabajo'

@description('Location for everything except Static Web Apps, which has its own list.')
param location string = resourceGroup().location

@description('Static Web Apps is not available in every region.')
@allowed(['westeurope', 'eastus2', 'centralus', 'westus2', 'eastasia'])
param staticWebAppLocation string = 'westeurope'

@description('''
How Cosmos is billed.

  free         1000 RU/s and 25 GB, $0 - but ONE account per subscription,
               and the slot does not release the moment you delete the
               account holding it. Azure can take hours.
  serverless   pay per request. Cents per month at demo volume, and no
               subscription-wide limit to collide with.
  provisioned  fixed RU/s, roughly $24/month at the 400 RU/s minimum.

This cannot be changed on an existing account - switching means creating a
new one and moving the data.
''')
@allowed(['free', 'serverless', 'provisioned'])
param cosmosBilling string = 'free'

@description('F1 is free but has no Always On and a 60 CPU-minute daily quota. B1 removes both for about USD 13/month.')
@allowed(['F1', 'B1'])
param appServiceSku string = 'F1'

@description('Domain used to send transactional email. Empty provisions an Azure-managed domain instead.')
param emailDomain string = ''

@description('Who gets the cost alert. Empty falls back to whoever holds Owner on the subscription.')
param budgetContactEmails array = []

@description('''
Resource group for the Function App. It has to be a different one: Azure will
not create a Linux Consumption plan in a group that already holds a Linux
Free/Basic plan, and the API runs on Linux F1. Create it before deploying.
''')
param functionsResourceGroupName string

var cosmosAccountName = '${appName}-cosmos-${uniqueString(resourceGroup().id)}'
var storageAccountName = take('${replace(appName, '-', '')}st${uniqueString(resourceGroup().id)}', 24)
var appServicePlanName = '${appName}-plan'
var apiName = '${appName}-api'
var functionsName = '${appName}-func'
var staticWebAppName = '${appName}-web'
// Communication Services names are reserved globally, and a reservation
// outlives a failed deployment - so these carry the group's unique suffix
// rather than being a bare, guessable string.
var communicationName = '${appName}-comms-${uniqueString(resourceGroup().id)}'
var emailServiceName = '${appName}-email-${uniqueString(resourceGroup().id)}'
var logAnalyticsName = '${appName}-logs'
var appInsightsName = '${appName}-insights'
var databaseName = 'MiFuturoTrabajo'

// ---------------------------------------------------------------------------
// Observability
// ---------------------------------------------------------------------------

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    // The free grant is 5 GB/month; 30 days is well inside it for this volume.
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------------------------------------------------------------------------
// Cosmos DB - free tier: 1000 RU/s and 25 GB, one account per subscription
// ---------------------------------------------------------------------------

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: cosmosAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: cosmosBilling == 'free'
    consistencyPolicy: {
      // Session is the default and the right one here: every read is a user
      // reading their own writes.
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: cosmosBilling == 'serverless' ? [ { name: 'EnableServerless' } ] : []
    backupPolicy: {
      type: 'Periodic'
      periodicModeProperties: {
        backupIntervalInMinutes: 1440
        backupRetentionIntervalInHours: 48
        backupStorageRedundancy: 'Local'
      }
    }
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmos
  name: databaseName
  properties: {
    resource: { id: databaseName }
    // A serverless account rejects a throughput setting outright: capacity is
    // per request, not reserved. Otherwise database-level shared throughput, so
    // all seven containers share one allocation rather than each demanding its
    // own 400 RU/s.
    options: cosmosBilling == 'serverless' ? {} : { throughput: 1000 }
  }
}

// Containers are created by the API at startup via EnsureCreatedAsync, which
// keeps the partition keys defined in exactly one place - the EF model - rather
// than duplicated here where they could drift.

// ---------------------------------------------------------------------------
// Storage - CV documents, and the Functions runtime's own bookkeeping
// ---------------------------------------------------------------------------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    // No anonymous access anywhere in the account: CVs are private, and reads
    // go through a short-lived user-delegation SAS.
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource cvContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'cv-uploads'
  properties: { publicAccess: 'None' }
}

// ---------------------------------------------------------------------------
// Communication Services - transactional email
// ---------------------------------------------------------------------------

// Declared after the domains below, because linkedDomains references one.
// Without that link the resource provisions cleanly and every send fails at
// runtime - the domain can be fully DNS-verified and still not be usable,
// which is exactly what happened the first time.
resource communication 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: communicationName
  location: 'global'
  properties: {
    dataLocation: 'Europe'
    linkedDomains: [ linkedDomainId ]
  }
}

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: emailServiceName
  location: 'global'
  properties: {
    dataLocation: 'Europe'
  }
}

// An Azure-managed domain sends immediately with no DNS work, but its mail
// frequently lands in spam. A custom domain needs DKIM/SPF records added by
// hand after deployment, and lands in the inbox.
resource azureManagedDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = if (empty(emailDomain)) {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource customDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = if (!empty(emailDomain)) {
  parent: emailService
  name: emailDomain
  location: 'global'
  properties: {
    domainManagement: 'CustomerManaged'
    userEngagementTracking: 'Disabled'
  }
}

// Resolved once, after the domain resources: the managed-domain resource only
// exists when emailDomain is empty, so the reference sits behind that same
// condition.
var linkedDomainId = empty(emailDomain) ? azureManagedDomain!.id : customDomain!.id

var senderAddress = empty(emailDomain)
  ? 'DoNotReply@${azureManagedDomain!.properties.mailFromSenderDomain}'
  : 'DoNotReply@${emailDomain}'

// ---------------------------------------------------------------------------
// Compute
// ---------------------------------------------------------------------------

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: appServiceSku
    tier: appServiceSku == 'F1' ? 'Free' : 'Basic'
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource api 'Microsoft.Web/sites@2023-12-01' = {
  name: apiName
  location: location
  kind: 'app,linux'
  identity: {
    // System-assigned identity is how the API reaches Storage and ACS without
    // a key living in configuration.
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      // F1 does not support Always On; setting it there fails the deployment.
      alwaysOn: appServiceSku != 'F1'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/health'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }

        { name: 'Persistence__Provider', value: 'Cosmos' }
        { name: 'CosmosDb__ConnectionString', value: cosmos.listConnectionStrings().connectionStrings[0].connectionString }
        { name: 'CosmosDb__DatabaseName', value: databaseName }

        // Managed identity, so no account key appears here.
        { name: 'AzureStorage__AccountUrl', value: storage.properties.primaryEndpoints.blob }

        { name: 'AzureCommunicationServices__ConnectionString', value: communication.listKeys().primaryConnectionString }
        { name: 'AzureCommunicationServices__SenderAddress', value: senderAddress }

        { name: 'BlazorClient__Url', value: 'https://${staticWebApp.properties.defaultHostname}' }

        { name: 'Jwt__Issuer', value: 'MiFuturoTrabajo' }
        { name: 'Jwt__Audience', value: 'MiFuturoTrabajo.Client' }

        // Jwt__SecretKey, Demo__Password and the OAuth client IDs are set
        // outside this template - see infra/README.md.
      ]
    }
  }
}

// Deployed into its own resource group - see infra/functions.bicep for why.
module functionsApp 'functions.bicep' = {
  name: 'functions'
  scope: resourceGroup(functionsResourceGroupName)
  params: {
    location: location
    functionsName: functionsName
    appInsightsConnectionString: appInsights.properties.ConnectionString
    storageConnectionString: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
    cosmosConnectionString: cosmos.listConnectionStrings().connectionStrings[0].connectionString
    databaseName: databaseName
    senderAddress: senderAddress
    communicationConnectionString: communication.listKeys().primaryConnectionString
  }
}

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppName
  location: staticWebAppLocation
  sku: {
    // Free includes a custom domain and managed TLS.
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // Deployment is driven by GitHub Actions rather than by the built-in
    // provider, so the workflow stays the single place a deploy happens.
    stagingEnvironmentPolicy: 'Enabled'
    allowConfigFileUpdates: true
  }
}

// ---------------------------------------------------------------------------
// RBAC - the point of the managed identities above
// ---------------------------------------------------------------------------

var storageBlobDataContributor = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource apiStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, api.id, storageBlobDataContributor)
  properties: {
    roleDefinitionId: storageBlobDataContributor
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Cost guardrail
// ---------------------------------------------------------------------------

// Everything above is free-tier or consumption, so any real spend means
// something is wrong - a tier changed, or the free grant was exhausted.
resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: '${appName}-budget'
  properties: {
    category: 'Cost'
    amount: 5
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: '2026-09-01'
    }
    notifications: {
      halfway: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        contactRoles: ['Owner']
        contactEmails: budgetContactEmails
        thresholdType: 'Actual'
      }
      exceeded: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        contactRoles: ['Owner']
        contactEmails: budgetContactEmails
        thresholdType: 'Forecasted'
      }
    }
  }
}

// ---------------------------------------------------------------------------

output apiUrl string = 'https://${api.properties.defaultHostName}'
output clientUrl string = 'https://${staticWebApp.properties.defaultHostname}'
output functionsAppName string = functionsApp.outputs.name
output apiAppName string = api.name
output staticWebAppName string = staticWebApp.name
output cosmosAccountName string = cosmos.name
output storageAccountName string = storage.name
output apiPrincipalId string = api.identity.principalId
output functionsPrincipalId string = functionsApp.outputs.principalId
