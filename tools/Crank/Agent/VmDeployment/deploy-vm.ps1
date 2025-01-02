#!/usr/bin/env pwsh

[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]
    $SubscriptionName,

    [Parameter(Mandatory = $true)]
    [string]
    $BaseName,

    [string]
    $NamePostfix = '',

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

$resourceGroupName = "FunctionsCrank-$OsType-$BaseName$NamePostfix"
$vmName = "functions-crank-$($OsType -eq 'windows' ? 'win' : $OsType)-$BaseName$NamePostfix".ToLower()
Write-Verbose "Creating VM '$vmName' in resource group '$resourceGroupName'"

Set-AzContext -Subscription $SubscriptionName | Out-Null

New-AzResourceGroup -Name $resourceGroupName -Location $Location | Out-Null

$vaultSubscriptionId = (Get-AzSubscription -SubscriptionName 'Antares-Demo').Id

$customScriptParameters = @{
    CrankBranch = 'master'
    Docker = $Docker.IsPresent
}

New-AzResourceGroupDeployment `
    -ResourceGroupName $resourceGroupName `
    -TemplateFile "$PSScriptRoot\template.json" `
    -TemplateParameterObject @{
        vmName = $vmName
        dnsLabelPrefix = $vmName
        vmSize = $VmSize
        osDiskType = $OsDiskType
        osType = $OsType
        adminUsername = $UserName
        authenticationType = $OsType -eq 'Windows' ? 'password' : 'sshPublicKey'
        vaultName = 'functions-crank-kv'
        vaultResourceGroupName = 'FunctionsCrank'
        vaultSubscription = $vaultSubscriptionId
        secretName = $OsType -eq 'Windows' ? 'CrankAgentVMAdminPassword' : 'LinuxCrankAgentVmSshKey-Public'
        customScriptParameters = $customScriptParameters | ConvertTo-Json -Compress
    } | Out-Null

Write-Verbose 'Restarting the VM...'
Restart-AzVM -ResourceGroupName $resourceGroupName -Name $vmName | Out-Null
Start-Sleep -Seconds 30

Write-Host "The crank VM is ready: $vmName"
