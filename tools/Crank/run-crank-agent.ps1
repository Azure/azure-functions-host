$ErrorActionPreference = 'Stop'

Set-MpPreference -DisableRealtimeMonitoring $true
Add-MpPreference -ExclusionProcess 'Microsoft.Azure.WebJobs.Script.WebHost.exe'

crank-agent
