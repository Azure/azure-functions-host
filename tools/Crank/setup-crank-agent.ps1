$ErrorActionPreference = 'Stop'

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

    $action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/C C:\dotnet-tools\crank-agent.exe 2>&1 >> $logsDir\crank-agent.log"
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

    Register-ScheduledTask `
        -TaskName "CrankAgent" -Description "Start crank-agent" `
        -Action $action -Trigger $trigger `
        @auth
}

#####################################

InstallDotNet
InstallCrankAgent
ScheduleCrankAgentStart -Credential (Get-Credential)
