<#
.SYNOPSIS
Generates the Linux compute-separation placeholder JIT trace.

.DESCRIPTION
Publishes the WebHost runtime, Functions.WorkerProxy, and MockWorker for a Linux RID, starts the runtime in placeholder
mode with external workers enabled, drives assign-first and link-first worker assignment/link scenarios, collects runtime
nettraces with dotnet-trace, runs the cold-start analyzer with JIT trace generation enabled, and merges the generated
scenario traces into src\WebJobs.Script.WebHost\PreJIT\linux.computeseparation.coldstart.jittrace.

The final checked-in trace should be generated on Linux for linux-x64. Functions.WorkerProxy is native AOT, so the
generation machine needs the native AOT toolchain, including clang and zlib development headers.

JIT trace generation uses Microsoft.Azure.Functions.ColdStartProfileAnalyzer, which shells out to the debug dotnet-pgo
create-jittrace command from dotnet/runtime. Build it with:
  ./build.sh -subset clr.tools -c Debug /p:BuildDotNetPgo=true
Then pass -DotNetPgoPath with the generated dotnet-pgo executable or its containing directory.
#>

param(
    [ValidateSet("assign-first", "link-first", "both")]
    [string]$Scenario = "both",

    [string]$Configuration = "Release",

    [string]$RuntimeIdentifier = "linux-x64",

    [string]$ArtifactsRoot = "",

    [string]$AzureWebJobsStorage = $env:AzureWebJobsStorage,

    [int]$RuntimeHttpPort = 7071,

    [int]$RuntimeGrpcPort = 50051,

    [int]$WorkerGrpcPort = 50052,

    [int]$HttpProxyPort = 50053,

    [int]$ManagementPort = 50054,

    [string]$MasterKey = "dev-master-key",

    [string]$WorkerIdPrefix = "jittrace-worker",

    [string]$TraceDuration = "00:00:00:30",

    [string]$DotNetTraceProviderArguments = "--providers Microsoft-Diagnostics-DiagnosticSource:0xfffffffffffff7ff:5,Microsoft-Windows-DotNETRuntime:0xC0001801F9:5,FunctionsSystemLogsEventSource:0xFFFFFFFFFFFFFFFF:5",

    [string]$DotNetPgoPath = "",

    [switch]$SkipPublish,

    [switch]$SkipAnalyzer,

    [switch]$KeepProcesses
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$traceFileName = "linux.computeseparation.coldstart.jittrace"
$traceFilePath = Join-Path $repoRoot "src\WebJobs.Script.WebHost\PreJIT\$traceFileName"
$analyzerDotNetPgoPath = if ($IsWindows)
{
    "C:\azure_functions_temp\artifacts\bin\coreclr\windows.x64.Debug\dotnet-pgo\dotnet-pgo.exe"
}
else
{
    "/var/tmp/azure_functions_temp/artifacts/bin/coreclr/linux.x64.Debug/dotnet-pgo/dotnet-pgo"
}

if ([string]::IsNullOrWhiteSpace($ArtifactsRoot))
{
    $ArtifactsRoot = Join-Path $repoRoot "artifacts\compute-separation-jittrace"
}
elseif (-not [System.IO.Path]::IsPathRooted($ArtifactsRoot))
{
    $ArtifactsRoot = Join-Path $repoRoot $ArtifactsRoot
}

$ArtifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsRoot)

if ([string]::IsNullOrWhiteSpace($AzureWebJobsStorage))
{
    Write-Warning "AzureWebJobsStorage was not provided. Falling back to UseDevelopmentStorage=true; start Azurite or pass -AzureWebJobsStorage for local generation."
    $AzureWebJobsStorage = "UseDevelopmentStorage=true"
}

if ($IsWindows -and [string]::Equals($RuntimeIdentifier, "linux-x64", [StringComparison]::OrdinalIgnoreCase))
{
    Write-Warning "The default linux-x64 trace should be generated on a Linux machine or CI image that has the native AOT toolchain for the worker proxy."
}

$runId = [DateTime]::UtcNow.ToString("yyyyMMddHHmmss")
$runRoot = Join-Path $ArtifactsRoot $runId
$publishRoot = Join-Path $runRoot "publish"
$logsRoot = Join-Path $runRoot "logs"
$scenarioRoot = Join-Path $runRoot "scenarios"

New-Item -ItemType Directory -Path $publishRoot, $logsRoot, $scenarioRoot -Force | Out-Null

function Invoke-CommandChecked
{
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory = $repoRoot
    )

    Write-Host ">>> $FilePath $($Arguments -join ' ')"
    $originalLocation = Get-Location
    try
    {
        Set-Location $WorkingDirectory
        $output = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }

        if ($exitCode -ne 0)
        {
            throw "Command failed with exit code $exitCode`: $FilePath $($Arguments -join ' ')"
        }
    }
    finally
    {
        Set-Location $originalLocation
    }
}

function Ensure-DotNetTool
{
    param(
        [string]$PackageName,
        [switch]$Prerelease
    )

    $toolList = & dotnet tool list --global
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to list global dotnet tools."
    }

    if ($toolList -match "(?m)^\s*$([regex]::Escape($PackageName))\s+")
    {
        return
    }

    $arguments = @("tool", "install", "--global", $PackageName)
    if ($Prerelease)
    {
        $arguments += "--prerelease"
    }

    Invoke-CommandChecked -FilePath "dotnet" -Arguments $arguments -WorkingDirectory $repoRoot
}

function Ensure-DotNetPgoForAnalyzer
{
    if ($SkipAnalyzer)
    {
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($DotNetPgoPath))
    {
        $resolvedPath = (Resolve-Path $DotNetPgoPath).Path
        $sourceDirectory = if ([System.IO.Directory]::Exists($resolvedPath))
        {
            $resolvedPath
        }
        else
        {
            Split-Path $resolvedPath -Parent
        }

        $destinationDirectory = Split-Path $analyzerDotNetPgoPath -Parent
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceDirectory "*") -Destination $destinationDirectory -Recurse -Force

        if (-not $IsWindows)
        {
            & chmod +x $analyzerDotNetPgoPath
        }
    }

    if (-not (Test-Path $analyzerDotNetPgoPath))
    {
        throw "dotnet-pgo was not found at '$analyzerDotNetPgoPath'. Build dotnet/runtime with './build.sh -subset clr.tools -c Debug /p:BuildDotNetPgo=true', then rerun this script with -DotNetPgoPath pointing to the generated dotnet-pgo executable or directory."
    }
}

function Publish-Project
{
    param(
        [string]$ProjectPath,
        [string]$OutputPath,
        [string[]]$ExtraArguments = @()
    )

    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

    $arguments = @(
        "publish",
        $ProjectPath,
        "-c",
        $Configuration,
        "-o",
        $OutputPath
    ) + $ExtraArguments

    Invoke-CommandChecked -FilePath "dotnet" -Arguments $arguments
}

function Get-EntryPoint
{
    param(
        [string]$OutputPath,
        [string]$AssemblyName
    )

    $nativePath = Join-Path $OutputPath $AssemblyName
    $exePath = Join-Path $OutputPath "$AssemblyName.exe"
    $dllPath = Join-Path $OutputPath "$AssemblyName.dll"

    if (Test-Path $nativePath)
    {
        return [pscustomobject]@{
            FilePath = $nativePath
            Arguments = @()
        }
    }

    if (Test-Path $exePath)
    {
        return [pscustomobject]@{
            FilePath = $exePath
            Arguments = @()
        }
    }

    if (Test-Path $dllPath)
    {
        return [pscustomobject]@{
            FilePath = "dotnet"
            Arguments = @($dllPath)
        }
    }

    throw "Could not find an entry point for $AssemblyName under $OutputPath."
}

function ConvertTo-PowerShellLiteral
{
    param([string]$Value)

    return "'$($Value.Replace("'", "''"))'"
}

function Start-LoggedProcess
{
    param(
        [string]$Name,
        [string]$FilePath,
        [string[]]$Arguments,
        [hashtable]$Environment = @{},
        [string]$WorkingDirectory = $repoRoot
    )

    $stdoutPath = Join-Path $logsRoot "$Name.out.log"
    $stderrPath = Join-Path $logsRoot "$Name.err.log"
    $pidPath = Join-Path $logsRoot "$Name.pid"
    Set-Content -Path $stdoutPath -Value "" -Encoding UTF8
    Set-Content -Path $stderrPath -Value "" -Encoding UTF8
    if (Test-Path $pidPath)
    {
        Remove-Item -Path $pidPath -Force
    }

    $argumentsLiteral = if ($Arguments.Count -eq 0)
    {
        "@()"
    }
    else
    {
        "@({0})" -f (($Arguments | ForEach-Object { ConvertTo-PowerShellLiteral $_ }) -join ", ")
    }

    $command = @"
`$process = Start-Process -FilePath {0} -ArgumentList {1} -WorkingDirectory {2} -RedirectStandardOutput {3} -RedirectStandardError {4} -NoNewWindow -PassThru
Set-Content -Path {5} -Value `$process.Id
`$process.WaitForExit()
exit `$process.ExitCode
"@ -f
        (ConvertTo-PowerShellLiteral $FilePath),
        $argumentsLiteral,
        (ConvertTo-PowerShellLiteral $WorkingDirectory),
        (ConvertTo-PowerShellLiteral $stdoutPath),
        (ConvertTo-PowerShellLiteral $stderrPath),
        (ConvertTo-PowerShellLiteral $pidPath)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Get-Process -Id $PID).Path
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    [void]$startInfo.ArgumentList.Add("-NoLogo")
    [void]$startInfo.ArgumentList.Add("-NoProfile")
    [void]$startInfo.ArgumentList.Add("-NonInteractive")
    [void]$startInfo.ArgumentList.Add("-Command")
    [void]$startInfo.ArgumentList.Add($command)

    foreach ($key in $Environment.Keys)
    {
        if ($null -eq $Environment[$key])
        {
            [void]$startInfo.Environment.Remove($key)
        }
        else
        {
            $startInfo.Environment[$key] = [string]$Environment[$key]
        }
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $process.EnableRaisingEvents = $true

    Write-Host "Starting $Name`: $FilePath $($Arguments -join ' ')"
    Write-Host "  stdout: $stdoutPath"
    Write-Host "  stderr: $stderrPath"
    [void]$process.Start()

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $pidPath))
    {
        if ($process.HasExited)
        {
            $stdout = if (Test-Path $stdoutPath) { Get-Content -Path $stdoutPath -Tail 80 | Out-String } else { "" }
            $stderr = if (Test-Path $stderrPath) { Get-Content -Path $stderrPath -Tail 80 | Out-String } else { "" }
            throw "$Name exited before writing its child process ID. Exit code: $($process.ExitCode).`nstdout:`n$stdout`nstderr:`n$stderr"
        }

        if ($stopwatch.Elapsed.TotalSeconds -gt 30)
        {
            $process.Kill($true)
            throw "Timed out waiting for $Name to write its child process ID to $pidPath."
        }

        Start-Sleep -Milliseconds 100
    }

    $childProcessId = [int](Get-Content -Path $pidPath -Raw)
    $childProcess = [System.Diagnostics.Process]::GetProcessById($childProcessId)

    return [pscustomobject]@{
        Name = $Name
        Process = $childProcess
        WrapperProcess = $process
        StdOutPath = $stdoutPath
        StdErrPath = $stderrPath
        PidPath = $pidPath
    }
}

function Dispose-LoggedProcess
{
    param([object]$Handle)

    if ($null -eq $Handle)
    {
        return
    }

    try
    {
        if ($null -ne $Handle.Process)
        {
            $Handle.Process.Dispose()
        }
    }
    finally
    {
        if ($null -ne $Handle.WrapperProcess)
        {
            if (-not $Handle.WrapperProcess.HasExited)
            {
                [void]$Handle.WrapperProcess.WaitForExit(2000)
            }

            if (-not $Handle.WrapperProcess.HasExited)
            {
                $Handle.WrapperProcess.Kill($true)
                [void]$Handle.WrapperProcess.WaitForExit(5000)
            }

            $Handle.WrapperProcess.Dispose()
        }
    }
}

function Stop-LoggedProcess
{
    param(
        [object]$Handle,
        [int]$TimeoutMilliseconds = 10000
    )

    if ($null -eq $Handle)
    {
        return
    }

    try
    {
        if (-not $Handle.Process.HasExited)
        {
            Write-Host "Stopping $($Handle.Name) (PID $($Handle.Process.Id))."
            $Handle.Process.Kill($true)
            [void]$Handle.Process.WaitForExit($TimeoutMilliseconds)
        }
    }
    finally
    {
        Dispose-LoggedProcess $Handle
    }
}

function Assert-LoggedProcessRunning
{
    param([object[]]$Handles)

    foreach ($handle in $Handles)
    {
        if ($null -eq $handle -or -not $handle.Process.HasExited)
        {
            continue
        }

        $stdout = if (Test-Path $handle.StdOutPath) { Get-Content -Path $handle.StdOutPath -Tail 80 | Out-String } else { "" }
        $stderr = if (Test-Path $handle.StdErrPath) { Get-Content -Path $handle.StdErrPath -Tail 80 | Out-String } else { "" }

        throw "$($handle.Name) exited early with code $($handle.Process.ExitCode).`nstdout:`n$stdout`nstderr:`n$stderr"
    }
}

function Wait-HttpStatus
{
    param(
        [string]$Uri,
        [int[]]$ExpectedStatusCodes = @(200),
        [hashtable]$Headers = @{},
        [string]$Method = "GET",
        [string]$Body = $null,
        [int]$TimeoutSeconds = 60,
        [object[]]$ProcessHandle = @()
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    {
        Assert-LoggedProcessRunning $ProcessHandle

        try
        {
            $parameters = @{
                Uri = $Uri
                Method = $Method
                Headers = $Headers
                TimeoutSec = 10
                ErrorAction = "Stop"
                SkipHttpErrorCheck = $true
            }

            if ($null -ne $Body)
            {
                $parameters["ContentType"] = "application/json"
                $parameters["Body"] = $Body
            }

            $response = Invoke-WebRequest @parameters
            if ($ExpectedStatusCodes -contains [int]$response.StatusCode)
            {
                return $response
            }

            Write-Host "$Method $Uri returned $($response.StatusCode); waiting for $($ExpectedStatusCodes -join ', ')."
        }
        catch
        {
            Write-Host "$Method $Uri failed: $($_.Exception.Message)"
        }

        Start-Sleep -Milliseconds 500
    }

    Assert-LoggedProcessRunning $ProcessHandle
    throw "Timed out waiting for $Method $Uri to return $($ExpectedStatusCodes -join ', ')."
}

function ConvertTo-Base64Url
{
    param([byte[]]$Bytes)

    return [Convert]::ToBase64String($Bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function New-WorkerProxySiteToken
{
    param(
        [string]$SigningKey,
        [string]$Audience
    )

    $header = @{ alg = "HS256"; typ = "JWT" } | ConvertTo-Json -Compress
    $payload = @{
        aud = $Audience
        iss = "https://legion.core.azurewebsites.net"
        exp = [DateTimeOffset]::UtcNow.AddHours(1).ToUnixTimeSeconds()
    } | ConvertTo-Json -Compress

    $encodedHeader = ConvertTo-Base64Url ([System.Text.Encoding]::UTF8.GetBytes($header))
    $encodedPayload = ConvertTo-Base64Url ([System.Text.Encoding]::UTF8.GetBytes($payload))
    $unsignedToken = "$encodedHeader.$encodedPayload"

    $keyBytes = [Convert]::FromBase64String($SigningKey)
    $hmac = [System.Security.Cryptography.HMACSHA256]::new($keyBytes)
    try
    {
        $signature = ConvertTo-Base64Url ($hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($unsignedToken)))
        return "$unsignedToken.$signature"
    }
    finally
    {
        $hmac.Dispose()
    }
}

function Protect-AssignmentContext
{
    param(
        [string]$Json,
        [string]$EncryptionKey
    )

    $keyBytes = [Convert]::FromBase64String($EncryptionKey)
    $aes = [System.Security.Cryptography.Aes]::Create()
    try
    {
        $aes.Key = $keyBytes
        $aes.GenerateIV()

        $input = [System.Text.Encoding]::UTF8.GetBytes($Json)
        $memoryStream = [System.IO.MemoryStream]::new()
        $encryptor = $aes.CreateEncryptor($aes.Key, $aes.IV)
        $cryptoStream = [System.Security.Cryptography.CryptoStream]::new($memoryStream, $encryptor, [System.Security.Cryptography.CryptoStreamMode]::Write)
        try
        {
            $cryptoStream.Write($input, 0, $input.Length)
            $cryptoStream.FlushFinalBlock()
            $cipherText = $memoryStream.ToArray()
        }
        finally
        {
            $cryptoStream.Dispose()
            $encryptor.Dispose()
            $memoryStream.Dispose()
        }

        $sha = [System.Security.Cryptography.SHA256]::Create()
        try
        {
            $keyHash = $sha.ComputeHash($aes.Key)
            return "{0}.{1}.{2}" -f [Convert]::ToBase64String($aes.IV), [Convert]::ToBase64String($cipherText), [Convert]::ToBase64String($keyHash)
        }
        finally
        {
            $sha.Dispose()
        }
    }
    finally
    {
        $aes.Dispose()
    }
}

function New-JsonContent
{
    param([object]$Value)

    return ($Value | ConvertTo-Json -Depth 20 -Compress)
}

function Invoke-Json
{
    param(
        [string]$Uri,
        [string]$Method,
        [object]$Body,
        [hashtable]$Headers = @{}
    )

    $json = if ($null -ne $Body) { New-JsonContent $Body } else { $null }
    return Wait-HttpStatus -Uri $Uri -Method $Method -Body $json -Headers $Headers -ExpectedStatusCodes @(200, 202)
}

function Wait-HostRunning
{
    param(
        [string]$BaseUri,
        [string]$Key,
        [int]$TimeoutSeconds = 120,
        [object]$ProcessHandle = $null
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    {
        Assert-LoggedProcessRunning $ProcessHandle

        try
        {
            $response = Invoke-WebRequest -Uri "$BaseUri/admin/host/status?code=$Key" -Method "GET" -TimeoutSec 10 -ErrorAction Stop
            $status = $response.Content | ConvertFrom-Json
            if ([string]::Equals([string]$status.state, "Running", [StringComparison]::OrdinalIgnoreCase))
            {
                return
            }

            Write-Host "Host state is '$($status.state)' after $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1)) seconds."
        }
        catch
        {
            Write-Host "Host status check failed: $($_.Exception.Message)"
        }

        Start-Sleep -Milliseconds 500
    }

    Assert-LoggedProcessRunning $ProcessHandle
    throw "Timed out waiting for the runtime host to reach Running state."
}

function Start-DotNetTrace
{
    param(
        [int]$ProcessId,
        [string]$OutputPath
    )

    Ensure-DotNetTool -PackageName "dotnet-trace"

    $toolRoot = if ($IsWindows)
    {
        Join-Path $env:USERPROFILE ".dotnet\tools"
    }
    else
    {
        Join-Path $env:HOME ".dotnet/tools"
    }

    $toolName = if ($IsWindows) { "dotnet-trace.exe" } else { "dotnet-trace" }
    $toolPath = Join-Path $toolRoot $toolName
    if (-not (Test-Path $toolPath))
    {
        throw "dotnet-trace was not found at $toolPath after installation."
    }

    $traceArguments = @("collect", "-p", [string]$ProcessId) + ($DotNetTraceProviderArguments -split " ") + @("-o", $OutputPath, "--duration", $TraceDuration)
    return Start-LoggedProcess -Name "dotnet-trace-$ProcessId" -FilePath $toolPath -Arguments $traceArguments
}

function Invoke-Analyzer
{
    param([string]$NetTracePath)

    # Invoke dotnet-pgo create-jittrace directly across the whole trace instead
    # of going through Microsoft.Azure.Functions.ColdStartProfileAnalyzer, which
    # narrows the window to a ~300ms cold-start span and filters out the assign,
    # link, and worker-connection methods we want to capture.
    $jitTracePath = [System.IO.Path]::ChangeExtension($NetTracePath, ".jittrace")

    Invoke-CommandChecked -FilePath $analyzerDotNetPgoPath -Arguments @(
        "create-jittrace",
        "--includeReadyToRun",
        "--sorted",
        "-t", $NetTracePath,
        "-o", $jitTracePath,
        "--verbose", "normal"
    ) -WorkingDirectory (Split-Path $NetTracePath -Parent)
}

function Invoke-Scenario
{
    param([string]$ScenarioName)

    $currentRuntimeHttpPort = $RuntimeHttpPort
    $currentRuntimeGrpcPort = $RuntimeGrpcPort
    $currentWorkerGrpcPort = $WorkerGrpcPort
    $currentHttpProxyPort = $HttpProxyPort
    $currentManagementPort = $ManagementPort

    if ($ScenarioName -eq "link-first")
    {
        $currentRuntimeHttpPort += 10
        $currentRuntimeGrpcPort += 10
        $currentWorkerGrpcPort += 10
        $currentHttpProxyPort += 10
        $currentManagementPort += 10
    }

    $scenarioOutput = Join-Path $scenarioRoot $ScenarioName
    $scriptRoot = Join-Path $scenarioOutput "wwwroot"
    $secretsPath = Join-Path $scenarioOutput "secrets"
    $standbySecretsPath = Join-Path ([System.IO.Path]::GetTempPath()) "functions\standby\secrets"
    $functionAppDirectory = Join-Path $scenarioOutput "functionApp"
    New-Item -ItemType Directory -Path $scenarioOutput, $scriptRoot, $secretsPath, $standbySecretsPath, $functionAppDirectory -Force | Out-Null
    Set-Content -Path (Join-Path $scriptRoot "host.json") -Value '{ "version": "2.0" }' -Encoding UTF8
    Set-Content -Path (Join-Path $functionAppDirectory "host.json") -Value '{ "version": "2.0" }' -Encoding UTF8

    $workerProxyAudience = "worker-proxy-compute-separation"
    $workerProxySigningKeyBytes = [byte[]]::new(32)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($workerProxySigningKeyBytes)
    $workerProxySigningKey = [Convert]::ToBase64String($workerProxySigningKeyBytes)
    $containerEncryptionKeyBytes = [byte[]]::new(32)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($containerEncryptionKeyBytes)
    $containerEncryptionKey = [Convert]::ToBase64String($containerEncryptionKeyBytes)

    $hostSecrets = @{
        masterKey = @{ name = "master"; value = $MasterKey; encrypted = $false }
        functionKeys = @()
        systemKeys = @()
    }
    Set-Content -Path (Join-Path $secretsPath "host.json") -Value (New-JsonContent $hostSecrets) -Encoding UTF8
    Set-Content -Path (Join-Path $standbySecretsPath "host.json") -Value (New-JsonContent $hostSecrets) -Encoding UTF8

    $runtimeEnv = @{
        AzureFunctionsWebHost__hostid = "jittracehost"
        AzureWebJobsScriptRoot = $scriptRoot
        AzureWebJobsStorage = $AzureWebJobsStorage
        AzureWebJobsSecretStorageType = "Files"
        FUNCTIONS_SECRETS_PATH = $secretsPath
        FUNCTIONS_WORKER_EXTERNAL_ENABLED = "true"
        FUNCTIONS_WORKER_RUNTIME = "node"
        WEBSITE_PLACEHOLDER_MODE = "1"
        WEBSITE_CONTAINER_READY = $null
        WEBSITE_SKU = "FlexConsumption"
        CONTAINER_ENCRYPTION_KEY = $containerEncryptionKey
        AZURE_FUNCTIONS_ENVIRONMENT = "Development"
        ASPNETCORE_URLS = "http://localhost:$currentRuntimeHttpPort"
        # JIT trace collection: disable ReadyToRun so methods that are composite-R2R
        # precompiled (controllers, WorkerConnectionManager, etc.) JIT during the
        # trace window and emit Method/JittingStarted events that dotnet-pgo
        # create-jittrace can record. Without this, R2R-precompiled methods execute
        # as native R2R code and never appear in the generated .jittrace.
        DOTNET_ReadyToRun = "0"
        DOTNET_TieredPGO = "1"
        DOTNET_TC_QuickJitForLoops = "1"
        DOTNET_TC_CallCountThreshold = "10000"
    }

    $workerProxyEnv = @{
        CONTAINER_ENCRYPTION_KEY = $workerProxySigningKey
        WEBSITE_POD_NAME = $workerProxyAudience
    }

    $runtimeEntryPoint = Get-EntryPoint -OutputPath $hostOutputPath -AssemblyName "Microsoft.Azure.WebJobs.Script.WebHost"
    $workerProxyEntryPoint = Get-EntryPoint -OutputPath $workerProxyOutputPath -AssemblyName "Microsoft.Azure.Functions.WorkerProxy"
    $mockWorkerEntryPoint = Get-EntryPoint -OutputPath $mockWorkerOutputPath -AssemblyName "MockWorker"

    $runtime = $null
    $workerProxy = $null
    $mockWorker = $null
    $trace = $null
    $netTracePath = Join-Path $scenarioOutput "$ScenarioName.nettrace"

    try
    {
        $runtime = Start-LoggedProcess -Name "runtime-$ScenarioName" -FilePath $runtimeEntryPoint.FilePath -Arguments ($runtimeEntryPoint.Arguments + @("--urls", "http://localhost:$currentRuntimeHttpPort")) -Environment $runtimeEnv
        Wait-HttpStatus -Uri "http://localhost:$currentRuntimeHttpPort/api/WarmUp" -ExpectedStatusCodes @(200, 202, 204) -TimeoutSeconds 300 -ProcessHandle $runtime | Out-Null

        # Start dotnet-trace before the worker connects so methods on the worker
        # connection path (WorkerConnectionService, RuntimeStateManager, etc.) JIT
        # inside the trace window.
        $trace = Start-DotNetTrace -ProcessId $runtime.Process.Id -OutputPath $netTracePath
        Start-Sleep -Seconds 2

        $workerProxyArguments = $workerProxyEntryPoint.Arguments + @(
            "--runtime-grpc-port", [string]$currentRuntimeGrpcPort,
            "--worker-grpc-port", [string]$currentWorkerGrpcPort,
            "--http-proxy-port", [string]$currentHttpProxyPort,
            "--management-port", [string]$currentManagementPort
        )
        $workerProxy = Start-LoggedProcess -Name "worker-proxy-$ScenarioName" -FilePath $workerProxyEntryPoint.FilePath -Arguments $workerProxyArguments -Environment $workerProxyEnv
        Wait-HttpStatus -Uri "http://localhost:$currentManagementPort/admin/worker/ready" -ExpectedStatusCodes @(200, 503) -TimeoutSeconds 60 -ProcessHandle $workerProxy | Out-Null

        $mockWorker = Start-LoggedProcess -Name "mock-worker-$ScenarioName" -FilePath $mockWorkerEntryPoint.FilePath -Arguments ($mockWorkerEntryPoint.Arguments + @("--grpc-endpoint", "http://localhost:$currentWorkerGrpcPort"))
        Wait-HttpStatus -Uri "http://localhost:$currentManagementPort/admin/worker/ready" -ExpectedStatusCodes @(200) -TimeoutSeconds 60 -ProcessHandle @($workerProxy, $mockWorker) | Out-Null

        $workerProxyHeaders = @{
            "x-ms-site-token" = New-WorkerProxySiteToken -SigningKey $workerProxySigningKey -Audience $workerProxyAudience
        }

        $workerAssignPayload = @{
            functionAppName = "test-compute-sep-app"
            functionAppId = 1234
            functionGroupName = "http"
            isAlwaysReady = $false
            environment = @{ FUNCTIONS_WORKER_RUNTIME = "node" }
            functionAppDirectory = $functionAppDirectory
        }

        $assignmentContext = @{
            SiteId = 1234
            SiteName = "test-compute-sep-app"
            Environment = @{
                FUNCTIONS_WORKER_EXTERNAL_ENABLED = "true"
                FUNCTIONS_WORKER_RUNTIME = "node"
                WEBSITE_SITE_NAME = "test-compute-sep-app"
            }
        }
        $encryptedContext = Protect-AssignmentContext -Json (New-JsonContent $assignmentContext) -EncryptionKey $containerEncryptionKey
        Invoke-Json -Uri "http://localhost:$currentRuntimeHttpPort/admin/instance/assign?code=$MasterKey" -Method "POST" -Body @{ encryptedContext = $encryptedContext } | Out-Null

        $workerId = "$WorkerIdPrefix-$ScenarioName"
        $linkPayload = @{
            WorkerPodName = $workerId
            WorkerHttpEndpoint = "http://localhost:$currentHttpProxyPort"
            WorkerGrpcEndpoint = "http://localhost:$currentRuntimeGrpcPort"
            WorkerContainerEncryptionKey = $workerProxySigningKey
        }

        if ($ScenarioName -eq "assign-first")
        {
            Invoke-Json -Uri "http://localhost:$currentManagementPort/admin/worker/assign" -Method "POST" -Headers $workerProxyHeaders -Body $workerAssignPayload | Out-Null
            Invoke-Json -Uri "http://localhost:$currentRuntimeHttpPort/admin/workers/${workerId}?code=$MasterKey" -Method "PUT" -Body $linkPayload | Out-Null
        }
        else
        {
            $linkJson = New-JsonContent $linkPayload
            $linkClient = [System.Net.Http.HttpClient]::new()
            try
            {
                $linkRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Put, "http://localhost:$currentRuntimeHttpPort/admin/workers/$workerId`?code=$MasterKey")
                $linkRequest.Content = [System.Net.Http.StringContent]::new($linkJson, [System.Text.Encoding]::UTF8, "application/json")
                $linkTask = $linkClient.SendAsync($linkRequest)
                Start-Sleep -Seconds 2
                Invoke-Json -Uri "http://localhost:$currentManagementPort/admin/worker/assign" -Method "POST" -Headers $workerProxyHeaders -Body $workerAssignPayload | Out-Null
                $linkResponse = $linkTask.GetAwaiter().GetResult()
                if (-not $linkResponse.IsSuccessStatusCode)
                {
                    throw "Link-first runtime link returned $([int]$linkResponse.StatusCode) $($linkResponse.ReasonPhrase)."
                }
            }
            finally
            {
                $linkClient.Dispose()
            }
        }

        Wait-HostRunning -BaseUri "http://localhost:$currentRuntimeHttpPort" -Key $MasterKey -ProcessHandle $runtime
        Wait-HttpStatus -Uri "http://localhost:$currentRuntimeHttpPort/api/HttpTrigger" -ExpectedStatusCodes @(200) -TimeoutSeconds 120 -ProcessHandle $runtime | Out-Null
    }
    finally
    {
        if ($null -ne $trace)
        {
            if (-not $trace.Process.WaitForExit(120000))
            {
                Stop-LoggedProcess $trace
            }
            else
            {
                Dispose-LoggedProcess $trace
            }
        }

        if (-not $KeepProcesses)
        {
            Stop-LoggedProcess $runtime
            Stop-LoggedProcess $mockWorker
            Stop-LoggedProcess $workerProxy
        }
    }

    if (-not $SkipAnalyzer)
    {
        Invoke-Analyzer -NetTracePath $netTracePath
    }

    return $scenarioOutput
}

$hostOutputPath = Join-Path $publishRoot "HostRuntime"
$workerProxyOutputPath = Join-Path $publishRoot "WorkerProxy"
$mockWorkerOutputPath = Join-Path $publishRoot "MockWorker"

if (-not $SkipPublish)
{
    Publish-Project -ProjectPath (Join-Path $repoRoot "src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj") -OutputPath $hostOutputPath -ExtraArguments @("-r", $RuntimeIdentifier)
    Publish-Project -ProjectPath (Join-Path $repoRoot "src\Functions.WorkerProxy\Functions.WorkerProxy.csproj") -OutputPath $workerProxyOutputPath -ExtraArguments @("-r", $RuntimeIdentifier)
    Publish-Project -ProjectPath (Join-Path $repoRoot "tools\ComputeSeparation\MockWorker\MockWorker.csproj") -OutputPath $mockWorkerOutputPath -ExtraArguments @("-r", $RuntimeIdentifier)
}

$publishedComputeTrace = Join-Path $hostOutputPath "PreJIT\$traceFileName"
if (Test-Path $publishedComputeTrace)
{
    Write-Host "Removing $publishedComputeTrace before collection so regeneration does not self-suppress compute-separation JIT events."
    Remove-Item $publishedComputeTrace -Force
}

Ensure-DotNetPgoForAnalyzer

$scenariosToRun = if ($Scenario -eq "both") { @("assign-first", "link-first") } else { @($Scenario) }
$scenarioOutputs = foreach ($scenarioToRun in $scenariosToRun)
{
    Invoke-Scenario -ScenarioName $scenarioToRun
}

if (-not $SkipAnalyzer)
{
    $jitTraceFiles = $scenarioOutputs | ForEach-Object { Get-ChildItem -Path $_ -Recurse -Filter "*.jittrace" }
    if (-not $jitTraceFiles)
    {
        throw "No .jittrace files were generated under $scenarioRoot."
    }

    Write-Host "Merging JIT trace files:"
    $jitTraceFiles | ForEach-Object { Write-Host "  $($_.FullName)" }

    $jitTraceFiles |
        Get-Content |
        Sort-Object -Unique |
        Set-Content $traceFilePath -Encoding UTF8

    $lineCount = (Get-Content $traceFilePath | Measure-Object -Line).Lines
    Write-Host "Wrote $traceFilePath with $lineCount unique entries."
}
else
{
    Write-Host "Skipped analyzer; raw nettrace files are under $scenarioRoot."
}
