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

& "$PSScriptRoot/deploy-vm.ps1" `
    -SubscriptionName $SubscriptionName `
    -BaseName $BaseName `
    -NamePostfixes $NamePostfixes `
    -OsType $OsType `
    -VmSize $VmSize `
    -OsDiskType $OsDiskType `
    -Location $Location `
    -UserName $UserName `
    -Verbose:$VerbosePreference

# TODO: remove this warning when app deployment is automated
$appPath = if ($OsType -eq 'Linux') { "/home/$UserName/FunctionApps" } else { 'C:\FunctionApps' }
Write-Warning "Remember to deploy the Function apps to $appPath"
