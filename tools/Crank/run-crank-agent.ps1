$ErrorActionPreference = 'Stop'

Set-MpPreference -DisableRealtimeMonitoring $true
Add-MpPreference -ExclusionProcess 'Microsoft.Azure.WebJobs.Script.WebHost.exe'
Add-MpPreference -ExclusionProcess 'crank-agent.exe'

$logsDir = 'C:\crank-agent-logs'
if (-not (Test-Path $logsDir -PathType Container)) {
    New-Item -Path $logsDir -ItemType Container
}

$logFileName = "$logsDir\crank-agent_$(Get-Date -Format 'yyyy-MM-dd_HH-mm-ss').log"

& 'C:\dotnet-tools\crank-agent.exe' 2>&1 >> $logFileName
