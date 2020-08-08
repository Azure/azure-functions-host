$ErrorActionPreference = 'Stop'

function InstallDotNet {
    Invoke-WebRequest 'https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.ps1' -OutFile 'dotnet-install.ps1'
    ./dotnet-install.ps1
}

function InstallCrankAgent {
    dotnet tool install -g Microsoft.Crank.Agent --version "0.1.0-*"
}

function ScheduleCrankAgentStart {
    $action = New-ScheduledTaskAction -Execute 'crank-agent'
    $trigger = New-ScheduledTaskTrigger -AtStartup

    Register-ScheduledTask `
        -TaskName "CrankAgent" -Description "Start crank-agent" `
        -Action $action -Trigger $trigger `
        -User 'Functions'
}

#####################################

InstallDotNet
InstallCrankAgent
ScheduleCrankAgentStart
