@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Environment name used to generate unique resource names')
param environmentName string

@description('Manager on-duty phone number for outbound PSTN calls (E.164 format, e.g. +14255550100)')
param managerPhoneNumber string

@description('ACS phone number for outbound caller ID (E.164 format, e.g. +14255550199)')
param acsPhoneNumber string

@description('Fabric Eventhouse KQL endpoint URL')
param fabricKustoEndpoint string

@description('Fabric KQL database name')
param fabricDatabaseName string

@description('Container image to deploy (e.g. ghcr.io/yourorg/smartfactorycallagent:latest)')
param containerImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

var resourceToken = toLower(uniqueString(subscription().id, resourceGroup().id, environmentName))
var tags = { 'azd-env-name': environmentName }

// ============================================================
// Key Vault
// ============================================================
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// ============================================================
// Managed Identity for Container App
// ============================================================
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${resourceToken}'
  location: location
  tags: tags
}

// Grant Key Vault Secrets User role to the managed identity
resource keyVaultSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, managedIdentity.id, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ============================================================
// Azure Communication Services
// ============================================================
resource acs 'Microsoft.Communication/communicationServices@2023-06-01-preview' = {
  name: 'acs-${resourceToken}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'United States'
  }
}

// Store ACS connection string in Key Vault
resource acsConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'acs-connection-string'
  properties: {
    value: acs.listKeys().primaryConnectionString
  }
}

// ============================================================
// Azure OpenAI
// ============================================================
resource openAi 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' = {
  name: 'oai-${resourceToken}'
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: 'oai-${resourceToken}'
    publicNetworkAccess: 'Enabled'
  }
}

resource gpt4oRealtimeDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-10-01-preview' = {
  parent: openAi
  name: 'gpt-4o-realtime'
  sku: {
    name: 'GlobalStandard'
    capacity: 10
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o-realtime-preview'
      version: '2024-12-17'
    }
  }
}

// Store OpenAI API key in Key Vault
resource openAiApiKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'openai-api-key'
  properties: {
    value: openAi.listKeys().key1
  }
}

// ============================================================
// Azure AI Foundry (AI Hub + Project)
// ============================================================
resource aiHub 'Microsoft.MachineLearningServices/workspaces@2024-04-01' = {
  name: 'hub-${resourceToken}'
  location: location
  tags: tags
  kind: 'Hub'
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    friendlyName: 'Smart Factory AI Hub'
    publicNetworkAccess: 'Enabled'
  }
}

resource aiProject 'Microsoft.MachineLearningServices/workspaces@2024-04-01' = {
  name: 'proj-${resourceToken}'
  location: location
  tags: tags
  kind: 'Project'
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    friendlyName: 'Smart Factory Project'
    hubResourceId: aiHub.id
    publicNetworkAccess: 'Enabled'
  }
}

// ============================================================
// Container Apps Environment
// ============================================================
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'log-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${resourceToken}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ============================================================
// Container App
// ============================================================
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-${resourceToken}'
  location: location
  tags: union(tags, { 'azd-service-name': 'SmartFactoryCallAgent' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      secrets: [
        {
          name: 'acs-connection-string'
          keyVaultUrl: acsConnectionStringSecret.properties.secretUri
          identity: managedIdentity.id
        }
        {
          name: 'openai-api-key'
          keyVaultUrl: openAiApiKeySecret.properties.secretUri
          identity: managedIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'smartfactorycallagent'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'Acs__ConnectionString'
              secretRef: 'acs-connection-string'
            }
            {
              name: 'Acs__PhoneNumber'
              value: acsPhoneNumber
            }
            {
              name: 'Acs__CallbackBaseUrl'
              value: 'https://ca-${resourceToken}.${containerAppsEnv.properties.defaultDomain}'
            }
            {
              name: 'OpenAi__Endpoint'
              value: openAi.properties.endpoint
            }
            {
              name: 'OpenAi__ApiKey'
              secretRef: 'openai-api-key'
            }
            {
              name: 'OpenAi__DeploymentName'
              value: 'gpt-4o-realtime'
            }
            {
              name: 'Foundry__ProjectEndpoint'
              value: aiProject.properties.discoveryUrl ?? ''
            }
            {
              name: 'Fabric__KustoEndpoint'
              value: fabricKustoEndpoint
            }
            {
              name: 'Fabric__DatabaseName'
              value: fabricDatabaseName
            }
            {
              name: 'ManagerPhoneNumber'
              value: managerPhoneNumber
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

// ============================================================
// Outputs
// ============================================================
output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output callbackUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}/api/callbacks'
output webSocketUrl string = 'wss://${containerApp.properties.configuration.ingress.fqdn}/ws/audio'
output alertWebhookUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}/api/alert'
output keyVaultName string = keyVault.name
output openAiEndpoint string = openAi.properties.endpoint
output acsResourceName string = acs.name
output managedIdentityClientId string = managedIdentity.properties.clientId
