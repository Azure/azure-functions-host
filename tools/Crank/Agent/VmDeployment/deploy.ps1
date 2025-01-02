#!/usr/bin/env pwsh

[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]
    $SubscriptionName,

    [Parameter(Mandatory = $true)]
    [string]
    $BaseName,

    [string[]]
    $NamePostfixes = @('-app', '-load'),

    [Parameter(Mandatory = $true)]
    [ValidateSet('Linux', 'Windows')]
    $OsType,

    [switch]
    $Docker,

    [string]
    $VmSize = 'Standard_E2s_v3',

    [string]
    $OsDiskType = 'Premium_LRS',

    [string]
    $Location = 'West Central US',

    [string]
    $UserName = 'Functions'
)

$ErrorActionPreference = 'Stop'

$NamePostfixes | ForEach-Object -Parallel {
    & "$using:PSScriptRoot/deploy-vm.ps1" `
        -SubscriptionName $using:SubscriptionName `
        -BaseName $using:BaseName `
        -NamePostfix $_ `
        -OsType $using:OsType `
        -Docker:$using:Docker `
        -VmSize $using:VmSize `
        -OsDiskType $using:OsDiskType `
        -Location $using:Location `
        -UserName $using:UserName `
        -Verbose:$using:VerbosePreference
}

# TODO: remove this warning when app deployment is automated
$appPath = if ($OsType -eq 'Linux') { "/home/$UserName/FunctionApps" } else { 'C:\FunctionApps' }
Write-Warning "Remember to deploy the Function apps to $appPath"
