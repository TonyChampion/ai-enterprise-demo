# Creates Development JWTs for the three demo users and writes them where the demo page loads them.
# Run from anywhere:  ./make-tokens.ps1
# If PowerShell blocks scripts:  powershell -ExecutionPolicy Bypass -File make-tokens.ps1
#
# alice  requester  customer_id=contoso
# bob    requester  customer_id=fabrikam
# dana   agent      (no customer: sees everything)
#
# These are signed with a key in YOUR user secrets and only work against the API on your machine,
# in Development. The output file is gitignored anyway: build the habit on tokens that don't matter.
$ErrorActionPreference = 'Stop'

$project  = Join-Path $PSScriptRoot 'src/Ticketing.Api'
$settings = Join-Path $project 'appsettings.Development.json'
$out      = Join-Path $project 'wwwroot/demo-tokens.json'
$users    = @(
    @{ Name = 'alice'; Role = 'requester'; Customer = 'contoso' },
    @{ Name = 'bob';   Role = 'requester'; Customer = 'fabrikam' },
    @{ Name = 'dana';  Role = 'agent';     Customer = $null }
)

# user-jwts rewrites appsettings.Development.json every run; put it back as it was
$backup = [IO.File]::ReadAllBytes($settings)
$tokens = [ordered]@{}
try {
    foreach ($u in $users) {
        $args = @('user-jwts', 'create', '--project', $project, '--name', $u.Name, '--role', $u.Role, '--valid-for', '30d', '--output', 'token')
        if ($u.Customer) { $args += @('--claim', "customer_id=$($u.Customer)") }
        $token = (& dotnet @args) -join ''
        if ($LASTEXITCODE -ne 0) { throw "Couldn't create a token for $($u.Name): $token" }
        $tokens[$u.Name] = $token.Trim()
        Write-Host "  $($u.Name) ($($u.Role)$(if ($u.Customer) { ", $($u.Customer)" }))"
    }
}
finally {
    [IO.File]::WriteAllBytes($settings, $backup)
}

[IO.File]::WriteAllText($out, ($tokens | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
Write-Host "Wrote $out (valid 30 days)"
