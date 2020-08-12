params (
    [bool]$InstallDotNet = $true,
    [bool]$InstallCrankAgent = $true
)

$ErrorActionPreference = 'Stop'

#region Utilities

function InstallDotNet {
    Invoke-WebRequest 'https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.ps1' -OutFile 'dotnet-install.ps1'
    ./dotnet-install.ps1
}

function InstallCrankAgent {
    dotnet tool install --tool-path c:\dotnet-tools Microsoft.Crank.Agent --version "0.1.0-*"
}

function ScheduleCrankAgentStart([pscredential]$Credential) {
    $logsDir = 'C:\crank-agent-logs'
    if (-not (Test-Path $logsDir -PathType Container)) {
        New-Item -Path $logsDir -ItemType Container
    }

    $runCrankAgentScriptPath = Join-Path `
                                    -Path (Split-Path $PSCommandPath -Parent) `
                                    -ChildPath 'run-crank-agent.ps1'

    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
                  -Argument "-NoProfile -WindowStyle Hidden -File $runCrankAgentScriptPath"

    $trigger = New-ScheduledTaskTrigger -AtStartup

    $auth =
        if ($Credential) {
            @{
                User = $Credential.UserName
                Password = $Credential.GetNetworkCredential().Password
            }
        } else {
            @{
                Principal = New-ScheduledTaskPrincipal -UserID "NT AUTHORITY\NETWORKSERVICE" `
                                -LogonType ServiceAccount -RunLevel Highest
            }
        }

    $null = Register-ScheduledTask `
                -TaskName "CrankAgent" -Description "Start crank-agent" `
                -Action $action -Trigger $trigger `
                @auth
}

#endregion

#region Main

if ($InstallDotNet) { InstallDotNet }
if ($InstallCrankAgent) { InstallCrankAgent }
ScheduleCrankAgentStart -Credential (Get-Credential)

#endregion
