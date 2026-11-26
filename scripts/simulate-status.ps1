<#
Simulates "the order system" publishing a status change to ONE user's private room.
Needs the Notifications__SimulationKey environment variable (run . .\scripts\dev-env.ps1 first).

Examples:
  .\scripts\simulate-status.ps1 -User alice -Order ord_1001 -Status shipped
  .\scripts\simulate-status.ps1 -User bob   -Order ord_2002 -Status delivered
#>
param(
    [Parameter(Mandatory = $true)][string]$User,
    [string]$Order = "ord_1001",
    [string]$Status = "shipped",
    [string]$Url = "http://localhost:59059/api/dev/simulate-status",
    [string]$ApiKey = $env:Notifications__SimulationKey
)

if (-not $ApiKey) {
    Write-Error "No simulation key. Run . .\scripts\dev-env.ps1 first."
    exit 1
}

$body = @{ userId = $User; orderId = $Order; status = $Status } | ConvertTo-Json -Compress

try {
    $response = Invoke-WebRequest -Uri $Url -Method Post -Body $body -ContentType "application/json" `
        -Headers @{ "X-Api-Key" = $ApiKey } -UseBasicParsing
    Write-Host ("Status: " + [int]$response.StatusCode)
    Write-Host $response.Content
}
catch [System.Net.WebException] {
    $errorResponse = $_.Exception.Response
    if ($errorResponse) {
        $reader = New-Object System.IO.StreamReader($errorResponse.GetResponseStream())
        Write-Host ("Status: " + [int]$errorResponse.StatusCode)
        Write-Host $reader.ReadToEnd()
    }
    else {
        Write-Host $_.Exception.Message
    }
}
