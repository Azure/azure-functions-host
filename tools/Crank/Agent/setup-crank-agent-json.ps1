#!/usr/bin/env pwsh

[CmdletBinding()]
param (
    [string]$ParametersJson
)

$ErrorActionPreference = 'Stop'

$parameters = @{}
($ParametersJson | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $parameters[$_.Name] = $_.Value }

& "$PSScriptRoot/setup-crank-agent.ps1" @parameters -Verbose
