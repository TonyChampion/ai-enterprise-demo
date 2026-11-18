# Deploys the demo environment and writes the API's local settings.
# Usage:  ./infra/deploy.ps1 -ResourceGroup rg-aiready-demo -Location eastus2 -PublisherEmail you@example.com
param(
    [string]$ResourceGroup = "rg-aiready-demo",
    [string]$Location = "eastus2",
    [Parameter(Mandatory = $true)][string]$PublisherEmail
)
$ErrorActionPreference = "Stop"

$me = az ad signed-in-user show --query id -o tsv
Write-Host "Presenter object ID: $me"

az group create -n $ResourceGroup -l $Location -o none

Write-Host "Deploying (API Management Basic v2 is the slow part, roughly 5-15 minutes)..."
$json = az deployment group create -g $ResourceGroup -n aiready `
    -f "$PSScriptRoot/main.bicep" `
    -p presenterPrincipalId=$me publisherEmail=$PublisherEmail `
    --query properties.outputs -o json
if ($LASTEXITCODE -ne 0 -or -not $json) {
    Write-Host "`nDeployment failed. Details:" -ForegroundColor Red
    az deployment operation group list -g $ResourceGroup -n aiready `
        --query "[?properties.provisioningState=='Failed'].{resource:properties.targetResource.resourceName, error:properties.statusMessage.error.message}" -o table
    throw "Fix the error above, then run deploy.ps1 again (it picks up where it left off)."
}
$out = ($json -join "`n") | ConvertFrom-Json

$foundryKey = az cognitiveservices account keys list -g $ResourceGroup -n $out.foundryName.value --query key1 -o tsv 2>$null

$settings = @{
    Ai = @{
        FoundryEndpoint  = $out.foundryEndpoint.value
        GatewayEndpoint  = $out.gatewayEndpoint.value
        FoundryKey       = $foundryKey
        TriageDeployment = "triage"
        ChatDeployment   = "chat"
    }
    APPLICATIONINSIGHTS_CONNECTION_STRING = $out.appInsightsConnectionString.value
}
$target = Join-Path $PSScriptRoot "../src/Ticketing.Api/appsettings.Local.json"
New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
$settings | ConvertTo-Json -Depth 5 | Set-Content $target
Write-Host "Wrote $target (gitignored)."
$out | ConvertTo-Json -Depth 5
