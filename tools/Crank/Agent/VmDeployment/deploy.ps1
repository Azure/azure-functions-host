#!/usr/bin/env pwsh

[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]
    $SubscriptionName,

    [Parameter(Mandatory = $true)]
    [string]
    $BaseName,

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

if ($OsType -ne 'Linux') {
    throw 'Only Linux is supported now'
}

$resourceGroupName = "FunctionsCrank-$OsType-$BaseName"
$vmName = "functions-crank-$OsType-$BaseName".ToLower()
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
        adminUsername = $UserName
        authenticationType = 'sshPublicKey'
        vaultName = 'functions-crank-kv'
        vaultResourceGroupName = 'FunctionsCrank'
        vaultSubscription = $vaultSubscriptionId
        secretName = 'LinuxCrankAgentVmSshKey-Public'
        customScriptParameters = $customScriptParameters | ConvertTo-Json -Compress
    }

Write-Verbose 'Restarting the VM...'
Restart-AzVM -ResourceGroupName $resourceGroupName -Name $vmName | Out-Null
Start-Sleep -Seconds 30

Write-Output "The crank VM is ready: $vmName"

# TODO: remove this warning when app deployment is automated
Write-Warning "Remember to deploy the Function apps to /home/$UserName/FunctionApps"
