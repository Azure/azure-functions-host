$ErrorActionPreference = 'Stop'

Set-MpPreference -DisableRealtimeMonitoring $true
Add-MpPreference -ExclusionProcess 'Microsoft.Azure.WebJobs.Script.WebHost.exe'
Add-MpPreference -ExclusionProcess 'crank-agent.exe'

crank-agent
