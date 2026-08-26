// The reminder Function App, in a resource group of its own.
//
// Not a stylistic split. Azure refuses to create a Linux **Consumption** (Y1)
// plan in a resource group that already holds a Linux Free/Basic plan:
//
//   Requested features 'Dynamic SKU, Linux Worker' not available in
//   resource group <name>. Please try using a different resource group.
//
// The API runs on Linux F1, so the two plans cannot share a group. Moving the
// whole deployment to a fresh group does not help - it is the combination, not
// the group's history. Hence a second group, holding only this.

param location string
param functionsName string
param appInsightsConnectionString string
param storageConnectionString string
param cosmosConnectionString string
param databaseName string
param senderAddress string
param communicationConnectionString string

@description('Cron for the daily task digest.')
param taskReminderSchedule string = '0 0 8 * * *'

@description('Cron for the interview reminder poll.')
param interviewReminderSchedule string = '0 */15 * * * *'

resource functionsPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${functionsName}-plan'
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  kind: 'functionapp'
  properties: {
    reserved: true
  }
}

resource functions 'Microsoft.Web/sites@2023-12-01' = {
  name: functionsName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: functionsPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'AzureWebJobsStorage', value: storageConnectionString }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }

        { name: 'CosmosDb__ConnectionString', value: cosmosConnectionString }
        { name: 'CosmosDb__DatabaseName', value: databaseName }

        { name: 'AzureCommunicationServices__ConnectionString', value: communicationConnectionString }
        { name: 'AzureCommunicationServices__SenderAddress', value: senderAddress }

        // In settings rather than in the attribute, so cadence changes - or
        // pausing a job by pointing it at a far-future expression - do not
        // need a redeploy.
        { name: 'TaskReminderSchedule', value: taskReminderSchedule }
        { name: 'InterviewReminderSchedule', value: interviewReminderSchedule }
      ]
    }
  }
}

output name string = functions.name
output principalId string = functions.identity.principalId
