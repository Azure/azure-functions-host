#!/usr/bin/env pwsh

[CmdletBinding()]
param (
    [bool]$InstallDotNet = $false,
    [bool]$InstallCrankAgent = $true,
    [string]$CrankBranch,
    [bool]$Docker = $false,
    [pscredential]$WindowsLocalAdmin
)

$ErrorActionPreference = 'Stop'

#region Utilities

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

    Write-Verbose 'Installing crank-agent tool...'

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
        '--version', '0.2.0-*'

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
    if ($Docker) {
        $crankRepoPath = CloneCrankRepo
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
        InstallCrankAgentTool        
    }

    if ($IsWindows) {
        New-NetFirewallRule -DisplayName 'Crank Agent' -Group 'Crank' -LocalPort 5010 -Protocol TCP -Direction Inbound -Action Allow | Out-Null
        New-NetFirewallRule -DisplayName 'Crank App & Load (inbound)' -Group 'Crank' -LocalPort 5000 -Protocol TCP -Direction Inbound -Action Allow | Out-Null
        New-NetFirewallRule -DisplayName 'Crank App & Load (outbound)' -Group 'Crank' -LocalPort 5000 -Protocol TCP -Direction Outbound -Action Allow | Out-Null
    }
}

function ScheduleCrankAgentAsWindowsService {
    param (
        [string]$ServiceName = "CrankAgentService"
    )

    $logPath = "C:\crank-agent-logs"
    $binPath = "`"C:\dotnet-tools\crank-agent.exe`" --service --log-path=$logPath"

    try {
        # Create the log directory if it doesn't exist
        if (-not (Test-Path -Path $logPath)) {
            New-Item -Path $logPath -ItemType Directory
        }

        # Disable real-time monitoring and exclude crank-agent.exe, functions host from Defender scans
        Set-MpPreference -DisableRealtimeMonitoring $true
        Add-MpPreference -ExclusionProcess 'crank-agent.exe'
        Add-MpPreference -ExclusionProcess 'Microsoft.Azure.WebJobs.Script.WebHost.exe'

        # Create the service
        sc.exe create $ServiceName binpath= $binPath
        sc.exe config $ServiceName start= auto

        # Verify the service creation
        sc.exe qc $ServiceName

        # Start the service
        sc.exe start $ServiceName
        Write-Host "Service '$ServiceName' started successfully."
    } catch {
        Write-Error "An error occurred while creating, querying, or starting the service: $_"
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
            ScheduleCrankAgentAsWindowsService
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
if ($InstallCrankAgent) { InstallCrankAgent }
ScheduleCrankAgentStart

#endregion
