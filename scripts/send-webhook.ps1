<#
Sends one signed webhook to the API, the way a real sender would.

Examples (the signing secret is read from the Webhook__Secret environment variable):
  .\scripts\send-webhook.ps1 -ProductId 1 -Delta 5
  .\scripts\send-webhook.ps1 -EventId evt_demo -ProductId 1 -Delta 5      # run twice: second is a duplicate
  .\scripts\send-webhook.ps1 -TimestampOffsetSeconds -3600                  # stale timestamp
  .\scripts\send-webhook.ps1 -Tamper                                        # body changed after signing
  .\scripts\send-webhook.ps1 -Secret wrong-secret                           # invalid signature
#>
param(
    [string]$Url = "http://localhost:59059/api/webhooks/events",
    [string]$Secret = $env:Webhook__Secret,
    [string]$EventId = ("evt_" + [guid]::NewGuid().ToString("N")),
    [int]$ProductId = 1,
    [int]$Delta = 5,
    [long]$TimestampOffsetSeconds = 0,
    [switch]$Tamper
)

if (-not $Secret) {
    Write-Error "No secret. Set `$env:Webhook__Secret or pass -Secret."
    exit 1
}

$json = '{"id":"' + $EventId + '","type":"stock.adjusted","data":{"productId":' + $ProductId + ',"delta":' + $Delta + '}}'
$jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($json)

# Signed bytes = ASCII(timestamp) + "." + exact body bytes
$timestamp = ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + $TimestampOffsetSeconds).ToString()
$signedBytes = [byte[]]([System.Text.Encoding]::UTF8.GetBytes($timestamp + ".") + $jsonBytes)

$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = [System.Text.Encoding]::UTF8.GetBytes($Secret)
$signature = ($hmac.ComputeHash($signedBytes) | ForEach-Object { $_.ToString("x2") }) -join ""

# -Tamper: send a different body than the one that was signed
$bodyToSend = $jsonBytes
if ($Tamper) {
    $bodyToSend = [System.Text.Encoding]::UTF8.GetBytes($json.Replace('"delta":' + $Delta, '"delta":999'))
}

$headers = @{
    "X-Webhook-Timestamp" = $timestamp
    "X-Webhook-Signature" = "sha256=$signature"
}

Write-Host "Sending event $EventId (timestamp $timestamp)"
try {
    $response = Invoke-WebRequest -Uri $Url -Method Post -Body $bodyToSend -ContentType "application/json" -Headers $headers -UseBasicParsing
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
