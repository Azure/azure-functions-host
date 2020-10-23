#!/usr/bin/env pwsh

[CmdletBinding()]
param (
    [string]$ParametersJsonBase64,
    [string]$WindowsLocalAdminUserName,
    [string]$WindowsLocalAdminPasswordBase64 # not a SecureString because we'll need to pass it via pwsh command line args
)

function GetWindowsLocalAdminCredential {
    $plaintextPassword = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($WindowsLocalAdminPasswordBase64))
    $securePassword = ConvertTo-SecureString -String $plaintextPassword -AsPlainText -Force
    [pscredential]::new($WindowsLocalAdminUserName, $securePassword)
}

$ErrorActionPreference = 'Stop'

$parametersJson = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($ParametersJsonBase64))

Write-Verbose "setup-crank-agent-raw.ps1: `$parametersJson: '$parametersJson' $WindowsLocalAdminUserName ***" -Verbose

$parameters = @{}
($parametersJson | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $parameters[$_.Name] = $_.Value }

$credential = $IsWindows ? @{ WindowsLocalAdmin = GetWindowsLocalAdminCredential } : @{ }

& "$PSScriptRoot/setup-crank-agent.ps1" @parameters @credential -Verbose
