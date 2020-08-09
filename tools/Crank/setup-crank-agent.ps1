$ErrorActionPreference = 'Stop'

function InstallDotNet {
    Invoke-WebRequest 'https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.ps1' -OutFile 'dotnet-install.ps1'
    ./dotnet-install.ps1
}

function InstallCrankAgent {
    dotnet tool install -g Microsoft.Crank.Agent --version "0.1.0-*"
}

function ScheduleCrankAgentStart {
    $logsDir = 'C:\crank-agent-logs'
    if (-not (Test-Path $logsDir -PathType Container)) {
        New-Item -Path $logsDir -ItemType Container
    }

    $action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/C crank-agent 2>&1 >> $logsDir\crank-agent.log"
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserID "NT AUTHORITY\NETWORKSERVICE" -LogonType ServiceAccount -RunLevel Highest

    Register-ScheduledTask `
        -TaskName "CrankAgent" -Description "Start crank-agent" `
        -Action $action -Trigger $trigger `
        -Principal $principal
}

#####################################

InstallDotNet
InstallCrankAgent
ScheduleCrankAgentStart
