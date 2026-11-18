# Demo 1: turn key auth off (or back on) on the Foundry resource.
# Usage:  ./infra/local-auth.ps1 -Disable      (keys stop working)
#         ./infra/local-auth.ps1 -Enable       (reset for the next rehearsal)
param(
    [string]$ResourceGroup = "rg-aiready-demo",
    [switch]$Disable,
    [switch]$Enable
)
$ErrorActionPreference = "Stop"
if ($Disable -eq $Enable) { throw "Pass exactly one of -Disable or -Enable" }
$name = az cognitiveservices account list -g $ResourceGroup --query "[?kind=='AIServices'].name | [0]" -o tsv
$value = if ($Disable) { "true" } else { "false" }
az resource update -g $ResourceGroup -n $name --resource-type "Microsoft.CognitiveServices/accounts" `
    --set properties.disableLocalAuth=$value -o none
az cognitiveservices account show -g $ResourceGroup -n $name --query "{name:name, disableLocalAuth:properties.disableLocalAuth}" -o table
Write-Host "Can take a minute to take effect. Test it before you say it on stage."
