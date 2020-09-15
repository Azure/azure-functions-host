#!/usr/bin/env pwsh

[CmdletBinding()]
param (
    [bool]$InstallDotNet = $true,
    [bool]$InstallCrankAgent = $true
)

$ErrorActionPreference = 'Stop'

#region Utilities

function InstallDotNet {
    Write-Verbose 'Installing dotnet...'
    if ($IsWindows) {
        Invoke-WebRequest 'https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.ps1' -OutFile 'dotnet-install.ps1'
        ./dotnet-install.ps1
    } else {
        # From https://docs.microsoft.com/dotnet/core/install/linux-ubuntu#install-the-sdk
        sudo apt-get update
        sudo apt-get install -y apt-transport-https
        sudo apt-get update
        sudo apt-get install -y dotnet-sdk-3.1
    }
}

function InstallCrankAgent {
    Write-Verbose 'Installing crank-agent...'
    if ($IsWindows) {
        dotnet tool install --tool-path c:\dotnet-tools Microsoft.Crank.Agent --version "0.1.0-*"
    } else {
        dotnet tool install -g Microsoft.Crank.Agent --version "0.1.0-*"
    }
}

function ScheduleCrankAgentStartWindows($RunScriptPath, [pscredential]$Credential) {
    $taskName = 'CrankAgent'

    if (Get-ScheduledTask -TaskName $taskName) {
        Write-Warning "Task '$taskName' already exists, no changes performed"
    } else {
        $action = New-ScheduledTaskAction -Execute 'pwsh.exe' `
                    -Argument "-NoProfile -WindowStyle Hidden -File $RunScriptPath"

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
                    -TaskName $taskName -Description "Start crank-agent" `
                    -Action $action -Trigger $trigger `
                    @auth
    }
}

function ScheduleCrankAgentStartLinux($RunScriptPath) {
    $currentCrontabContent = (crontab -l) ?? $null
    if ($currentCrontabContent -match '\bcrank-agent\b') {
        Write-Warning "crank-agent reference is found in crontab, no changes performed"
    } else {
        $currentCrontabContent, "@reboot $RunScriptPath" | crontab -
    }
}

function ScheduleCrankAgentStart {
    Write-Verbose 'Scheduling crank-agent start...'

    $scriptPath = Join-Path -Path (Split-Path $PSCommandPath -Parent) -ChildPath 'run-crank-agent.ps1'

    if ($IsWindows) {
        ScheduleCrankAgentStartWindows -RunScriptPath $scriptPath -Credential (Get-Credential)
    } else {
        ScheduleCrankAgentStartLinux -RunScriptPath $scriptPath
    }

    Write-Warning 'Please reboot to start crank-agent'
}

#endregion

#region Main

if ($InstallDotNet) { InstallDotNet }
if ($InstallCrankAgent) { InstallCrankAgent }
ScheduleCrankAgentStart

#endregion
