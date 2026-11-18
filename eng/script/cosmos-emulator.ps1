param(
    [Parameter(Mandatory)]
    [ValidateSet('Start', 'Stop')]
    [string] $Action,
    [Parameter(Mandatory)]
    [string] $ContainerName,
    [Parameter(Mandatory)]
    [string] $DiagnosticsDirectory
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $DiagnosticsDirectory -Force | Out-Null
Start-Transcript -Path (Join-Path $DiagnosticsDirectory "$Action.log") -Force

try {
    $wslc = Join-Path $env:ProgramFiles 'WSL\wslc.exe'
    $certificateState = Join-Path $DiagnosticsDirectory 'certificate-thumbprint.txt'
    $containerState = Join-Path $DiagnosticsDirectory 'container-name.txt'

    function Invoke-Wslc {
        & $wslc @args
        if ($LASTEXITCODE -ne 0) {
            throw "wslc $($args -join ' ') failed with exit code $LASTEXITCODE."
        }
    }

    if ($Action -eq 'Stop') {
        try {
            if (Test-Path $containerState) {
                # Only this job's container is owned here, not the shared WSL session.
                & $wslc inspect --type container $ContainerName
                if ($LASTEXITCODE -eq 0) {
                    & $wslc logs --timestamps $ContainerName
                    if ($LASTEXITCODE -ne 0) {
                        Write-Warning "Unable to collect emulator logs (exit code $LASTEXITCODE)."
                    }

                    Invoke-Wslc rm --force $ContainerName
                    Remove-Item $containerState
                }
                else {
                    Write-Warning "Container $ContainerName was not found or could not be inspected (exit code $LASTEXITCODE)."
                }
            }
        }
        finally {
            if (Test-Path $certificateState) {
                $certificatePath = (Get-Content $certificateState -Raw).Trim()
                if (Test-Path $certificatePath) {
                    Remove-Item $certificatePath
                }
                Remove-Item $certificateState
            }
        }

        return
    }

    Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber | Format-List
    Get-CimInstance Win32_ComputerSystem | Select-Object HypervisorPresent, TotalPhysicalMemory | Format-List
    Get-CimInstance Win32_Processor |
        Select-Object Name, VirtualizationFirmwareEnabled, SecondLevelAddressTranslationExtensions | Format-List

    $windowsEmulator = Join-Path $env:ProgramFiles 'Azure Cosmos DB Emulator\Microsoft.Azure.Cosmos.Emulator.exe'
    if (Test-Path $windowsEmulator) {
        (Get-Item $windowsEmulator).VersionInfo | Select-Object FileName, FileVersion, ProductVersion | Format-List
    }
    else {
        Write-Host 'The Windows Cosmos DB emulator is not installed.'
    }

    & wsl.exe --version
    Write-Host "wsl --version exit code: $LASTEXITCODE"
    if (-not (Test-Path $wslc)) {
        throw 'WSL containers are unavailable. The agent image needs WSL 2.9.3 or newer, VirtualMachinePlatform, and virtualization support. No provisioned Cosmos fallback is permitted.'
    }

    Invoke-Wslc version
    $image = 'mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest'
    $started = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-Wslc pull $image
    Invoke-Wslc image inspect $image

    Set-Content -Path $containerState -Value $ContainerName
    Invoke-Wslc run --detach --name $ContainerName `
        --publish 127.0.0.1:8081:8081 --publish 127.0.0.1:8080:8080 `
        $image --protocol https --enable-explorer false --enable-telemetry false
    Invoke-Wslc inspect --type container $ContainerName

    & curl.exe --fail --silent --show-error --retry 60 --retry-delay 2 --retry-all-errors `
        --connect-timeout 5 --max-time 5 --retry-max-time 180 http://localhost:8080/ready
    if ($LASTEXITCODE -ne 0) {
        throw "Cosmos emulator readiness failed with curl exit code $LASTEXITCODE."
    }

    # Bootstrap trust from the job-owned loopback endpoint; SDK requests still validate TLS.
    $tcp = [System.Net.Sockets.TcpClient]::new()
    $ssl = $null
    $certificate = $null
    try {
        $null = $tcp.ConnectAsync('localhost', 8081).WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
        $ssl = [System.Net.Security.SslStream]::new($tcp.GetStream(), $false, { $true })
        $ssl.ReadTimeout = 10000
        $ssl.WriteTimeout = 10000
        $ssl.AuthenticateAsClient('localhost')
        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($ssl.RemoteCertificate)
        $certificateFile = Join-Path $DiagnosticsDirectory 'emulator.cer'
        [System.IO.File]::WriteAllBytes($certificateFile, $certificate.Export(
            [System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        $certificatePath = "Cert:\LocalMachine\Root\$($certificate.Thumbprint)"
        if (-not (Test-Path $certificatePath)) {
            $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
            $principal = [Security.Principal.WindowsPrincipal]::new($identity)
            if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
                throw 'The CI agent must run elevated to trust the emulator certificate in LocalMachine\Root. CurrentUser\Root may require an interactive confirmation, which is not supported in CI.'
            }

            Set-Content -Path $certificateState -Value $certificatePath
            Import-Certificate -FilePath $certificateFile -CertStoreLocation Cert:\LocalMachine\Root | Out-Host
        }
    }
    finally {
        if ($null -ne $certificate) { $certificate.Dispose() }
        if ($null -ne $ssl) { $ssl.Dispose() }
        $tcp.Dispose()
    }

    Write-Host "Cosmos emulator ready after $($started.Elapsed). HTTPS endpoint: https://localhost:8081/"
}
finally {
    Stop-Transcript
}
