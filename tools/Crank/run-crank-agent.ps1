$ErrorActionPreference = 'Stop'

if ($IsWindows) {
    Set-MpPreference -DisableRealtimeMonitoring $true
    Add-MpPreference -ExclusionProcess 'Microsoft.Azure.WebJobs.Script.WebHost.exe'
    Add-MpPreference -ExclusionProcess 'crank-agent.exe'
}

$logsDir = $IsWindows ? 'C:\crank-agent-logs' : '/home/Functions/crank-agent-logs'
if (-not (Test-Path $logsDir -PathType Container)) {
    New-Item -Path $logsDir -ItemType Container > $null
}

$logFileName = Join-Path -Path $logsDir -ChildPath "$(Get-Date -Format 'yyyy-MM-dd_HH-mm-ss').log"

$invokeCrankAgentCommand = $IsWindows ? 'C:\dotnet-tools\crank-agent.exe' : '/home/Functions/.dotnet/tools/crank-agent';

& $invokeCrankAgentCommand 2>&1 >> $logFileName
