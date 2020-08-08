$ErrorActionPreference = 'Stop'

Set-MpPreference -DisableRealtimeMonitoring $true
if (Get-Service WinDefend) {
    Stop-Service WinDefend
}

Add-MpPreference -ExclusionProcess 'Microsoft.Azure.WebJobs.Script.WebHost.exe'

crank-agent
