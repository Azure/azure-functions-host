#!/usr/bin/env pwsh

[CmdletBinding()]
param (
    [bool]$InstallDotNet = $true,
    [bool]$InstallCrankAgent = $true,
    [string]$CrankBranch,
    [bool]$Docker = $false,
    [pscredential]$WindowsLocalAdmin
)

$ErrorActionPreference = 'Stop'

#region Utilities

function InstallDotNet {
    Write-Verbose 'Installing dotnet...'
    if ($IsWindows) {
        Invoke-WebRequest 'https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.ps1' -OutFile 'dotnet-install.ps1'
        $dotnetInstallDir = "$env:ProgramFiles\dotnet"
        ./dotnet-install.ps1 -InstallDir $dotnetInstallDir
        [Environment]::SetEnvironmentVariable("Path", $env:Path + ";$dotnetInstallDir\;", 'Machine')
    } else {
        # From https://docs.microsoft.com/dotnet/core/install/linux-ubuntu#install-the-sdk
        sudo apt-get update
        sudo apt-get install -y apt-transport-https
        sudo apt-get update
        sudo apt-get install -y dotnet-sdk-3.1
    }
}

function BuildCrankAgent($CrankRepoPath) {
    Push-Location $CrankRepoPath
    try {
        $logFileName = 'build.log'
        Write-Verbose "Building crank (see $(Join-Path -Path $PWD -ChildPath $logFileName))..."
        $buildCommand = $IsWindows ? '.\build.cmd' : './build.sh'
        & $buildCommand -configuration Release -pack > $logFileName
        if (-not $?) {
            throw "Crank build failed, exit code: $LASTEXITCODE"
        }

        Join-Path -Path $PWD -ChildPath "artifacts/packages/Release/Shipping"
    } finally {
        Pop-Location
    }
}

function GetDotNetToolsLocationArgs {
    $IsWindows ? ('--tool-path', 'c:\dotnet-tools') : '-g'
}

function InstallCrankAgentTool($LocalPackageSource) {
    Write-Verbose 'Stopping crank-agent...'

    $crankAgentProcessName = 'crank-agent'
    if (Get-Process -Name $crankAgentProcessName -ErrorAction SilentlyContinue) {
        Stop-Process -Name $crankAgentProcessName -Force
    }

    Write-Verbose 'Uninstalling crank-agent...'

    $uninstallArgs = 'tool', 'uninstall', 'Microsoft.Crank.Agent'
    $uninstallArgs += GetDotNetToolsLocationArgs
    & dotnet $uninstallArgs

    Write-Verbose 'Installing crank-agent...'

    $installArgs =
        'tool', 'install', 'Microsoft.Crank.Agent',
        '--version', '0.1.0-*'

    $installArgs += GetDotNetToolsLocationArgs

    if ($LocalPackageSource) {
        $installArgs += '--add-source', $LocalPackageSource
    }

    Write-Verbose "Invoking dotnet with arguments: $installArgs"
    & dotnet $installArgs
}

function EnsureDirectoryExists($Path) {
    if (-not (Test-Path -PathType Container -Path $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function CloneCrankRepo {
    Write-Verbose "Cloning crank repo..."
    $githubParent = $IsLinux ? '~' : 'C:\'
    $githubPath = Join-Path -Path $githubParent -ChildPath 'github'
    EnsureDirectoryExists $githubPath
    Push-Location -Path $githubPath
    try {
        git clone https://github.com/dotnet/crank.git | Out-Null
        Set-Location crank
        if ($CrankBranch) {
            git checkout $CrankBranch | Out-Null
        }
        $PWD.Path
    } finally {
        Pop-Location
    }
}

function InstallCrankAgent {
    $crankRepoPath = CloneCrankRepo

    if ($Docker) {
        Push-Location $crankRepoPath/docker/agent
        try {
            # Build the docker-agent image
            ./build.sh

            # Build the functions-docker-agent image
            Set-Location $PSScriptRoot/Linux/Docker
            ./build.sh
        } finally {
            Pop-Location
        }
    } else {
        if ($CrankBranch) {
            $packagesDirectory = BuildCrankAgent -CrankRepoPath $crankRepoPath
            InstallCrankAgentTool -LocalPackageSource $packagesDirectory
        } else {
            InstallCrankAgentTool
        }
    }

    if ($IsWindows) {
        New-NetFirewallRule -DisplayName 'Crank Agent' -Group 'Crank' -LocalPort 5010 -Protocol TCP -Direction Inbound -Action Allow | Out-Null
        New-NetFirewallRule -DisplayName 'Crank App & Load (inbound)' -Group 'Crank' -LocalPort 5000 -Protocol TCP -Direction Inbound -Action Allow | Out-Null
        New-NetFirewallRule -DisplayName 'Crank App & Load (outbound)' -Group 'Crank' -LocalPort 5000 -Protocol TCP -Direction Outbound -Action Allow | Out-Null
    }
}

function ScheduleCrankAgentStartWindows($RunScriptPath, [pscredential]$Credential) {
    $taskName = 'CrankAgent'

    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
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
    if ($Docker) {
        Write-Verbose 'Starting crank-agent...'

        $functionAppsPath = Join-Path -Path '~' -ChildPath 'FunctionApps'
        EnsureDirectoryExists -Path $functionAppsPath
        $helloAppPath = Join-Path -Path $functionAppsPath -ChildPath 'HelloApp'
        EnsureDirectoryExists -Path $helloAppPath
        
        & "$PSScriptRoot/Linux/Docker/run.sh"
    } else {
        Write-Verbose 'Scheduling crank-agent start...'

        $scriptPath = Join-Path -Path (Split-Path $PSCommandPath -Parent) -ChildPath 'run-crank-agent.ps1'

        if ($IsWindows) {
            ScheduleCrankAgentStartWindows -RunScriptPath $scriptPath -Credential $WindowsLocalAdmin
        } else {
            ScheduleCrankAgentStartLinux -RunScriptPath $scriptPath
        }

        Write-Warning 'Please reboot to start crank-agent'
    }
}

function InstallDocker {
    Write-Verbose 'Installing Docker...'
    if ($IsWindows) {
        throw 'Using Docker on Windows is not supported yet'
    } else {
        & "$PSScriptRoot/Linux/install-docker.sh"
    }
}

#endregion

#region Main

Write-Verbose "WindowsLocalAdmin: '$($WindowsLocalAdmin.UserName)'"

if ($Docker) { InstallDocker }
if ($InstallDotNet) { InstallDotNet }
if ($InstallCrankAgent) { InstallCrankAgent }
ScheduleCrankAgentStart

#endregion
