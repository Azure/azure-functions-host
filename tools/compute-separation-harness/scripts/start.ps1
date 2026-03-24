<#
.SYNOPSIS
    Launches the compute-separation E2E harness (Sidecar + MockWorker + Runtime).

.DESCRIPTION
    Builds and starts the Sidecar relay, MockWorker, and Functions Runtime in
    external-worker mode. Pass -NoMockWorker to skip the mock worker and attach
    a real language worker manually (see README.md for instructions).

    Press Ctrl+C to stop all processes.

.PARAMETER RuntimeGrpcPort
    Port the sidecar exposes for the runtime's gRPC connection. Default: 50051.

.PARAMETER WorkerGrpcPort
    Port the sidecar exposes for the language worker's gRPC connection. Default: 50052.

.PARAMETER HttpProxyPort
    Port the sidecar exposes for HTTP reverse-proxy traffic. Default: 50053.

.PARAMETER RuntimePort
    Port the Functions runtime listens on for HTTP requests. Default: 7071.

.PARAMETER SampleApp
    Optional path to a Functions sample app whose root contains a host.json.
    When provided, the runtime's AzureWebJobsScriptRoot is set to this path.

.PARAMETER NoMockWorker
    When set, the MockWorker is NOT started. Use this when you want to attach
    your own language worker manually.
#>
[CmdletBinding()]
param(
    [int]$RuntimeGrpcPort  = 50051,
    [int]$WorkerGrpcPort   = 50052,
    [int]$HttpProxyPort    = 50053,
    [int]$RuntimePort      = 7071,
    [string]$SampleApp     = "",
    [switch]$NoMockWorker
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot    = Resolve-Path (Join-Path $PSScriptRoot '..\..\..') | Select-Object -ExpandProperty Path
$sidecarProj = Join-Path $repoRoot 'tools\compute-separation-harness\Sidecar\Sidecar.csproj'
$mockWorkerProj = Join-Path $repoRoot 'tools\compute-separation-harness\MockWorker\MockWorker.csproj'
$runtimeProj = Join-Path $repoRoot 'src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj'

# ── Build ───────────────────────────────────────────────────────────────────────
Write-Host "`n=== Building Sidecar ===" -ForegroundColor Cyan
dotnet build $sidecarProj -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Sidecar build failed." }

if (-not $NoMockWorker) {
    Write-Host "`n=== Building MockWorker ===" -ForegroundColor Cyan
    dotnet build $mockWorkerProj -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "MockWorker build failed." }
}

Write-Host "`n=== Building Runtime ===" -ForegroundColor Cyan
dotnet build $runtimeProj -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Runtime build failed." }

# ── Process tracking ────────────────────────────────────────────────────────────
$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

function Stop-AllProcesses {
    Write-Host "`nStopping processes..." -ForegroundColor Yellow
    foreach ($proc in $processes) {
        if (-not $proc.HasExited) {
            try {
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                Write-Host "  Stopped PID $($proc.Id) ($($proc.ProcessName))"
            }
            catch {
                # Process may have already exited.
            }
        }
    }
}

# Handle Ctrl+C
$null = [Console]::TreatControlCAsInput
try {
    # ── Start Sidecar ───────────────────────────────────────────────────────
    Write-Host "`n=== Starting Sidecar ===" -ForegroundColor Green
    Write-Host "  Runtime gRPC : http://localhost:$RuntimeGrpcPort"
    Write-Host "  Worker  gRPC : http://localhost:$WorkerGrpcPort"
    Write-Host "  HTTP proxy   : http://localhost:$HttpProxyPort"

    $sidecarArgs = @(
        'run', '--project', $sidecarProj, '--no-build', '-c', 'Debug', '--',
        '--runtime-grpc-port', $RuntimeGrpcPort,
        '--worker-grpc-port',  $WorkerGrpcPort,
        '--http-proxy-port',   $HttpProxyPort
    )
    $sidecarProc = Start-Process -FilePath 'dotnet' -ArgumentList $sidecarArgs `
        -PassThru -NoNewWindow
    $processes.Add($sidecarProc)

    # Give the sidecar a moment to start listening.
    Start-Sleep -Seconds 3

    # ── Start MockWorker (optional) ─────────────────────────────────────────
    if (-not $NoMockWorker) {
        Write-Host "`n=== Starting MockWorker ===" -ForegroundColor Green
        Write-Host "  Worker gRPC  : http://localhost:$WorkerGrpcPort"

        $mockWorkerArgs = @(
            'run', '--project', $mockWorkerProj, '--no-build', '-c', 'Debug', '--',
            '--grpc-endpoint', "http://localhost:$WorkerGrpcPort"
        )
        $mockWorkerProc = Start-Process -FilePath 'dotnet' -ArgumentList $mockWorkerArgs `
            -PassThru -NoNewWindow
        $processes.Add($mockWorkerProc)

        # Give the worker a moment to connect.
        Start-Sleep -Seconds 2
    }

    # ── Start Runtime ───────────────────────────────────────────────────────
    Write-Host "`n=== Starting Runtime ===" -ForegroundColor Green
    Write-Host "  HTTP endpoint: http://localhost:$RuntimePort"
    Write-Host "  External gRPC: http://localhost:$RuntimeGrpcPort"

    # Use ProcessStartInfo so we can set per-process environment variables
    # without polluting the current session (compatible with PS 5.1+).
    $runtimeStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $runtimeStartInfo.FileName  = 'dotnet'
    $runtimeStartInfo.Arguments = "run --project `"$runtimeProj`" --no-build -c Debug"
    $runtimeStartInfo.UseShellExecute = $false

    $runtimeStartInfo.Environment['FUNCTIONS_WORKER_EXTERNAL_ENABLED']       = 'true'
    $runtimeStartInfo.Environment['FUNCTIONS_WORKER_EXTERNAL_GRPC_ENDPOINT'] = "http://localhost:$RuntimeGrpcPort"
    $runtimeStartInfo.Environment['ASPNETCORE_URLS']                         = "http://localhost:$RuntimePort"

    if ($SampleApp) {
        $runtimeStartInfo.Environment['AzureWebJobsScriptRoot'] = (Resolve-Path $SampleApp).Path
    }

    $runtimeProc = [System.Diagnostics.Process]::Start($runtimeStartInfo)
    $processes.Add($runtimeProc)

    # ── Instructions ────────────────────────────────────────────────────────
    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
    Write-Host "║  Compute Separation Harness is running                          ║" -ForegroundColor Magenta
    Write-Host "║                                                                  ║" -ForegroundColor Magenta
    if ($NoMockWorker) {
    Write-Host "║  Attach a language worker to the sidecar worker gRPC port:      ║" -ForegroundColor Magenta
    Write-Host "║    FUNCTIONS_GRPC_HOST=127.0.0.1                                ║" -ForegroundColor Magenta
    Write-Host "║    FUNCTIONS_GRPC_PORT=$WorkerGrpcPort                               ║" -ForegroundColor Magenta
    } else {
    Write-Host "║  MockWorker is connected (HttpTrigger function registered).     ║" -ForegroundColor Magenta
    }
    Write-Host "║                                                                  ║" -ForegroundColor Magenta
    Write-Host "║  Test with:                                                      ║" -ForegroundColor Magenta
    Write-Host "║    curl http://localhost:$RuntimePort/api/HttpTrigger                ║" -ForegroundColor Magenta
    Write-Host "║                                                                  ║" -ForegroundColor Magenta
    Write-Host "║  Press Ctrl+C to stop all processes.                            ║" -ForegroundColor Magenta
    Write-Host "╚══════════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta
    Write-Host ""

    # ── Wait loop ───────────────────────────────────────────────────────────
    while ($true) {
        if ([Console]::KeyAvailable) {
            $key = [Console]::ReadKey($true)
            if ($key.Key -eq 'C' -and $key.Modifiers -band [ConsoleModifiers]::Control) {
                break
            }
        }

        # Exit if any process has died unexpectedly.
        if ($sidecarProc.HasExited) {
            Write-Host "Sidecar exited with code $($sidecarProc.ExitCode)." -ForegroundColor Red
            break
        }
        if ($runtimeProc.HasExited) {
            Write-Host "Runtime exited with code $($runtimeProc.ExitCode)." -ForegroundColor Red
            break
        }
        if ((-not $NoMockWorker) -and $mockWorkerProc.HasExited) {
            Write-Host "MockWorker exited with code $($mockWorkerProc.ExitCode)." -ForegroundColor Red
            break
        }

        Start-Sleep -Milliseconds 250
    }
}
finally {
    [Console]::TreatControlCAsInput = $false
    Stop-AllProcesses
}
