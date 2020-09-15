#!/usr/bin/env pwsh

param(
    [string]
    $UserName = 'Functions'
)

$ErrorActionPreference = 'Stop'

if ($IsWindows) {
    Set-MpPreference -DisableRealtimeMonitoring $true
    Add-MpPreference -ExclusionProcess 'Microsoft.Azure.WebJobs.Script.WebHost.exe'
    Add-MpPreference -ExclusionProcess 'crank-agent.exe'
}

$logsDir = $IsWindows ? 'C:\crank-agent-logs' : "/home/$UserName/crank-agent-logs"
if (-not (Test-Path $logsDir -PathType Container)) {
    New-Item -Path $logsDir -ItemType Container > $null
}

$logFileName = Join-Path -Path $logsDir -ChildPath "$(Get-Date -Format 'yyyy-MM-dd_HH-mm-ss').log"
New-Item -Path $logFileName -ItemType File > $null

$stableLogFileName = Join-Path -Path $logsDir -ChildPath 'current.log'
if (Test-Path $stableLogFileName) {
    Remove-Item $stableLogFileName
}
New-Item -ItemType SymbolicLink -Path $stableLogFileName -Target $logFileName > $null

$invokeCrankAgentCommand = $IsWindows ? 'C:\dotnet-tools\crank-agent.exe' : "/home/$UserName/.dotnet/tools/crank-agent";

& $invokeCrankAgentCommand 2>&1 >> $logFileName
