// Architecting AI-Ready Enterprises on Azure: demo environment
// Foundry (AIServices) + project + 2 deployments, Content Safety, APIM (Basic v2) AI gateway,
// Log Analytics + Application Insights. No private networking: the demo runs the API locally.
// Tear it down when you're done: APIM bills while idle.

targetScope = 'resourceGroup'

@description('Region for everything. Must support the model below and Content Safety Prompt Shields.')
param location string = resourceGroup().location

@description('Short prefix for resource names.')
param prefix string = 'aiready'

@description('Object ID of the presenter (az ad signed-in-user show --query id -o tsv). Gets direct model access for the identity beat.')
param presenterPrincipalId string

@description('Publisher email required by API Management.')
param publisherEmail string

@description('Model used for both triage and chat deployments.')
param modelName string = 'gpt-4.1-mini'
param modelVersion string = '2025-04-14'
param modelSku string = 'GlobalStandard'

@description('Deployment capacity in thousands of tokens per minute.')
param modelCapacity int = 50

@description('Gateway token limit per caller per minute. Low on purpose so the rogue caller hits 429 quickly.')
param gatewayTokensPerMinute int = 10000

@description('Start with keys enabled so demo 1 beat 1 works. Flip with scripts/local-auth.ps1.')
param disableLocalAuth bool = false

var suffix = uniqueString(resourceGroup().id)
var foundryName = '${prefix}-foundry-${suffix}'
var safetyName = '${prefix}-safety-${suffix}'
var apimName = '${prefix}-apim-${suffix}'

// Built-in role definition IDs
var roleOpenAIUser = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd' // Cognitive Services OpenAI User
var roleCogServicesUser = 'a97b65f3-24c7-4388-baec-2e87135dc908' // Cognitive Services User

// ---------------------------------------------------------------- Monitoring
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-ai-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------------------------------------------------------------- Foundry
resource foundry 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: foundryName
  location: location
  kind: 'AIServices'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: foundryName
    allowProjectManagement: true
    disableLocalAuth: disableLocalAuth
    publicNetworkAccess: 'Enabled'
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundry
  name: 'ticketing'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    displayName: 'Ticketing'
    description: 'Support ticketing: triage and chat'
  }
}

// Prompt Shields on the deployment set to annotate, not block, so demo 2 can show
// the naive version obeying the attack and the GATEWAY doing the blocking.
// Harm categories stay at the default Medium threshold.
var harm = [ 'Hate', 'Sexual', 'Violence', 'Selfharm' ]
resource raiPolicy 'Microsoft.CognitiveServices/accounts/raiPolicies@2025-06-01' = {
  parent: foundry
  name: 'demo-annotate-shields'
  properties: {
    basePolicyName: 'Microsoft.DefaultV2'
    mode: 'Default'
    contentFilters: concat(
      map(harm, h => { name: h, blocking: true, enabled: true, severityThreshold: 'Medium', source: 'Prompt' }),
      map(harm, h => { name: h, blocking: true, enabled: true, severityThreshold: 'Medium', source: 'Completion' }),
      [
        { name: 'Jailbreak', blocking: false, enabled: true, source: 'Prompt' }
        { name: 'Indirect Attack', blocking: false, enabled: true, source: 'Prompt' }
      ]
    )
  }
  dependsOn: [ project ] // one operation at a time on the Foundry account
}

resource triageDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundry
  name: 'triage'
  sku: { name: modelSku, capacity: modelCapacity }
  properties: {
    model: { format: 'OpenAI', name: modelName, version: modelVersion }
    raiPolicyName: raiPolicy.name
  }
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundry
  name: 'chat'
  sku: { name: modelSku, capacity: modelCapacity }
  properties: {
    model: { format: 'OpenAI', name: modelName, version: modelVersion }
    raiPolicyName: raiPolicy.name
  }
  dependsOn: [ triageDeployment ] // deployments on one account must go one at a time
}

// ---------------------------------------------------------------- Content Safety
resource safety 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: safetyName
  location: location
  kind: 'ContentSafety'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: safetyName
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

// ---------------------------------------------------------------- API Management
resource apim 'Microsoft.ApiManagement/service@2024-05-01' = {
  name: apimName
  location: location
  sku: { name: 'BasicV2', capacity: 1 }
  identity: { type: 'SystemAssigned' }
  properties: {
    publisherEmail: publisherEmail
    publisherName: 'AI-Ready Enterprises demo'
  }
}

resource apimLogger 'Microsoft.ApiManagement/service/loggers@2024-05-01' = {
  parent: apim
  name: 'appinsights'
  properties: {
    loggerType: 'applicationInsights'
    resourceId: appInsights.id
    credentials: {
      instrumentationKey: appInsights.properties.InstrumentationKey
    }
  }
}

resource openAiBackend 'Microsoft.ApiManagement/service/backends@2024-06-01-preview' = {
  parent: apim
  name: 'foundry-openai'
  properties: {
    url: 'https://${foundryName}.openai.azure.com/openai'
    protocol: 'http'
    credentials: {
      #disable-next-line BCP037
      managedIdentity: { resource: 'https://cognitiveservices.azure.com' }
    }
  }
}

resource safetyBackend 'Microsoft.ApiManagement/service/backends@2024-06-01-preview' = {
  parent: apim
  name: 'content-safety'
  properties: {
    url: 'https://${safetyName}.cognitiveservices.azure.com'
    protocol: 'http'
    credentials: {
      #disable-next-line BCP037
      managedIdentity: { resource: 'https://cognitiveservices.azure.com' }
    }
  }
}

resource openAiApi 'Microsoft.ApiManagement/service/apis@2024-05-01' = {
  parent: apim
  name: 'openai'
  properties: {
    displayName: 'Model gateway (Azure OpenAI)'
    path: 'openai'
    protocols: [ 'https' ]
    subscriptionRequired: false
    serviceUrl: 'https://${foundryName}.openai.azure.com/openai'
  }
}

resource opPost 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = {
  parent: openAiApi
  name: 'post-any'
  properties: { displayName: 'POST /*', method: 'POST', urlTemplate: '/*' }
}

resource opGet 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = {
  parent: openAiApi
  name: 'get-any'
  properties: { displayName: 'GET /*', method: 'GET', urlTemplate: '/*' }
}

resource openAiPolicy 'Microsoft.ApiManagement/service/apis/policies@2024-05-01' = {
  parent: openAiApi
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: replace(replace(loadTextContent('gateway-policy.xml'), '{tenant-id}', tenant().tenantId), '{tokens-per-minute}', string(gatewayTokensPerMinute))
  }
  dependsOn: [ openAiBackend, safetyBackend, apimDiagnostics ]
}

resource apimDiagnostics 'Microsoft.ApiManagement/service/apis/diagnostics@2024-05-01' = {
  parent: openAiApi
  name: 'applicationinsights'
  properties: {
    loggerId: apimLogger.id
    alwaysLog: 'allErrors'
    sampling: { samplingType: 'fixed', percentage: 100 }
    verbosity: 'information'
    logClientIp: false
    httpCorrelationProtocol: 'W3C'
    metrics: true // required by llm-emit-token-metric
  }
}

// ---------------------------------------------------------------- Role assignments
// Gateway -> model (Foundry sees APIM's identity on this hop)
resource apimToFoundry 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: foundry
  name: guid(foundry.id, apim.id, roleOpenAIUser)
  properties: {
    principalId: apim.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleOpenAIUser)
  }
}

// Gateway -> Content Safety
resource apimToSafety 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: safety
  name: guid(safety.id, apim.id, roleCogServicesUser)
  properties: {
    principalId: apim.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleCogServicesUser)
  }
}

// Presenter -> model directly (demo 1 beat 2: DefaultAzureCredential, no key)
resource presenterToFoundry 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: foundry
  name: guid(foundry.id, presenterPrincipalId, roleOpenAIUser)
  properties: {
    principalId: presenterPrincipalId
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleOpenAIUser)
  }
}

// ---------------------------------------------------------------- Outputs
output foundryEndpoint string = 'https://${foundryName}.openai.azure.com/'
output foundryName string = foundry.name
output gatewayEndpoint string = apim.properties.gatewayUrl
output contentSafetyEndpoint string = 'https://${safetyName}.cognitiveservices.azure.com/'
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output appInsightsName string = appInsights.name
output apimName string = apim.name
