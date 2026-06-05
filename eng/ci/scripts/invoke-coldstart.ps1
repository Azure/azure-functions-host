# invoke-coldstart.ps1
# Triggers the cold start and measures latency.
#
# Windows (IIS): Single POST with ?forcespecialization — SpecializationSimulatorMiddleware
#   reads AzureWebJobsScriptRoot from the JSON body, triggers specialization, and forwards
#   the request to the function endpoint.
#
# Linux: Two-call approach matching production —
#   1. POST encrypted HostAssignmentContext to /admin/instance/assign (triggers specialization)
#   2. GET the function endpoint (measures cold-start latency)
#
# Encryption format matches EncryptionHelper.Encrypt():
#   {base64(IV)}.{base64(AES-CBC-ciphertext)}.{base64(SHA256(key))}

param(
    [Parameter(Mandatory)][ValidateSet('Windows','Linux')][string]$Os,
    [Parameter(Mandatory)][string]$FunctionInvocationUrl,
    [Parameter(Mandatory)][string]$FunctionAppOutputPath,
    [Parameter(Mandatory)][string]$SiteName
)

$ErrorActionPreference = 'Stop'

function Invoke-LinuxSpecialization {
    $encryptionKey = $env:ENCRYPTION_KEY
    if (-not $encryptionKey) {
        Write-Host "##vso[task.logissue type=error]ENCRYPTION_KEY not set. Cannot encrypt instance/assign payload."
        exit 1
    }

    # Convert key to byte array — matches SecretsUtility.ToKeyBytes():
    #   64-char hex string → 32 bytes; otherwise base64
    if ($encryptionKey.Length -eq 64 -and $encryptionKey -match '^[0-9a-fA-F]+$') {
        $keyBytes = [byte[]]::new(32)
        for ($i = 0; $i -lt 64; $i += 2) {
            $keyBytes[$i / 2] = [Convert]::ToByte($encryptionKey.Substring($i, 2), 16)
        }
        Write-Host "Encryption key parsed as hex (32 bytes)"
    } else {
        $keyBytes = [Convert]::FromBase64String($encryptionKey)
        Write-Host "Encryption key parsed as base64 ($($keyBytes.Length) bytes)"
    }

    # Build HostAssignmentContext (matches Models/HostAssignmentContext.cs)
    $assignmentContext = @{
        siteId           = 1
        siteName         = $SiteName
        environment      = @{ AzureWebJobsScriptRoot = $FunctionAppOutputPath }
        lastModifiedTime = (Get-Date).ToString("o")
    } | ConvertTo-Json -Depth 10 -Compress

    # Encrypt with AES-CBC
    $aes = [System.Security.Cryptography.Aes]::Create()
    $aes.Key = $keyBytes
    $aes.GenerateIV()
    $ivBase64 = [Convert]::ToBase64String($aes.IV)

    $plainBytes = [System.Text.Encoding]::UTF8.GetBytes($assignmentContext)
    $encryptor = $aes.CreateEncryptor($aes.Key, $aes.IV)
    $ms = [System.IO.MemoryStream]::new()
    $cs = [System.Security.Cryptography.CryptoStream]::new($ms, $encryptor, [System.Security.Cryptography.CryptoStreamMode]::Write)
    $cs.Write($plainBytes, 0, $plainBytes.Length)
    $cs.FlushFinalBlock()
    $cipherBytes = $ms.ToArray()
    $cs.Dispose(); $ms.Dispose(); $aes.Dispose()

    # SHA256 hash of key (host uses this to verify the correct key was used)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $keyHash = [Convert]::ToBase64String($sha256.ComputeHash($keyBytes))
    $sha256.Dispose()

    # Format: {base64(IV)}.{base64(ciphertext)}.{base64(SHA256(key))}
    $encryptedContext = "{0}.{1}.{2}" -f $ivBase64, [Convert]::ToBase64String($cipherBytes), $keyHash

    $assignUrl = "http://localhost:5000/admin/instance/assign?code=test"
    $assignPayload = @{ encryptedContext = $encryptedContext } | ConvertTo-Json -Compress

    Write-Host "Triggering specialization via $assignUrl"
    $specializationDuration = Measure-Command {
        $response = Invoke-WebRequest -Uri $assignUrl -Method Post -Body $assignPayload -ContentType "application/json" -ErrorAction Stop
    }
    Write-Host "Specialization response: $($response.StatusCode). Latency: $($specializationDuration.TotalMilliseconds) ms"
}

# ── Main ──────────────────────────────────────────────────────────────────────

if ($Os -eq "Linux") {
    Invoke-LinuxSpecialization

    # Measure cold-start latency (specialization already triggered above)
    Write-Host "Calling $FunctionInvocationUrl"
    $duration = Measure-Command {
        $response = Invoke-WebRequest -Uri $FunctionInvocationUrl -Method Get -ErrorAction Stop
    }
} else {
    # Windows: POST with AzureWebJobsScriptRoot in body + ?forcespecialization in URL
    $body = @{ AzureWebJobsScriptRoot = $FunctionAppOutputPath } | ConvertTo-Json

    Write-Host "Calling $FunctionInvocationUrl (POST with specialization payload)"
    Write-Host "Request body: $body"
    $duration = Measure-Command {
        $response = Invoke-WebRequest -Uri $FunctionInvocationUrl -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
    }
}

Write-Host "Response: $($response.StatusCode)"
Write-Host "Cold start latency: $($duration.TotalMilliseconds) ms"
Write-Host "##vso[task.setvariable variable=coldStartLatency;isOutput=true]$($duration.TotalMilliseconds)"
