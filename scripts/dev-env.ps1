<#
Sets the environment variables the API and the test client need, in THIS PowerShell window.
Run it with a leading dot, in EVERY window you use for the demo:

    . .\scripts\dev-env.ps1

Random secrets are generated once and kept in .dev-env.json, which is git-ignored.
Never commit that file, and never reuse these values anywhere real.
#>
$file = Join-Path $PSScriptRoot "..\.dev-env.json"

function New-Secret([int]$bytes = 32) {
    $buffer = New-Object byte[] $bytes
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $generator.GetBytes($buffer)
    return ([Convert]::ToBase64String($buffer) -replace '[^A-Za-z0-9]', '')
}

if (-not (Test-Path $file)) {
    @{
        jwtKey        = (New-Secret 48)
        alice         = (New-Secret 12)
        bob           = (New-Secret 12)
        simulationKey = (New-Secret 24)
        webhookSecret = (New-Secret 32)
    } | ConvertTo-Json | Set-Content $file
    Write-Host "Created $file (git-ignored)"
}

$secrets = Get-Content $file -Raw | ConvertFrom-Json
$env:Jwt__Key = $secrets.jwtKey
$env:Demo__Users__alice = $secrets.alice
$env:Demo__Users__bob = $secrets.bob
$env:Notifications__SimulationKey = $secrets.simulationKey
$env:Webhook__Secret = $secrets.webhookSecret

Write-Host "Environment ready: Jwt__Key, Demo__Users__alice, Demo__Users__bob, Notifications__SimulationKey, Webhook__Secret"
