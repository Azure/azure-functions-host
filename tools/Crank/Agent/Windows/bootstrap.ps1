param(
    [string]$ParametersJsonBase64,
    [string]$WindowsLocalAdminUserName,
    [string]$WindowsLocalAdminPasswordBase64)

$ErrorActionPreference = 'Stop'

# The user should have "Log on as a service" right to run psexec".

$tempFilePath = "C:\temp\secpol.cfg"

if (!(Test-Path -Path "C:\temp")) { New-Item -Path "C:\temp" -ItemType Directory | Out-Null }

try {
    # Export current security policy
    secedit /export /cfg $tempFilePath
    if (!(Test-Path -Path $tempFilePath)) { throw "Failed to export security policy." }

    # Read and update 'SeServiceLogonRight'
    $content = Get-Content $tempFilePath
    $entry = $content | Where-Object { $_ -match "^SeServiceLogonRight\s*=" }
    $updatedContent = if ($entry) {
        $values = ($entry -split "=")[1].Trim()
        if ($values -notmatch "\b$WindowsLocalAdminUserName\b") { $content -replace "^SeServiceLogonRight\s*=.*", "SeServiceLogonRight = $values,$WindowsLocalAdminUserName" } else { return }
    }
    else { $content + "`r`nSeServiceLogonRight = $WindowsLocalAdminUserName" }

    # Apply updated security policy
    $updatedContent | Set-Content $tempFilePath
    secedit /configure /db secedit.sdb /cfg $tempFilePath /areas USER_RIGHTS
    Write-Host "Successfully added 'Log on as a service' right for user '$WindowsLocalAdminUserName'." -ForegroundColor Green
}
catch {
    Write-Host "An error occurred: $_" -ForegroundColor Red
}
finally {
    if (Test-Path -Path $tempFilePath) { Remove-Item -Path $tempFilePath -Force }
}

# Install chocolatey
Set-ExecutionPolicy Bypass -Scope Process -Force
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
Invoke-Expression ((New-Object System.Net.WebClient).DownloadString('https://chocolatey.org/install.ps1'))
$chocoCmd = "$env:ProgramData\chocolatey\bin\choco.exe"

& $chocoCmd install -y git
$env:PATH += ";$env:ProgramFiles\Git\cmd"

& $chocoCmd install -y powershell-core
$env:PATH += ";$env:ProgramFiles\PowerShell\7"

& $chocoCmd install -y sysinternals --version 2024.12.16

& $chocoCmd install -y dotnet-sdk --version="8.0.404"
& $chocoCmd install -y dotnet-sdk --version="9.0.101"

# Clone azure-functions-host repo
$githubPath = 'C:\github'
New-Item -Path $githubPath -ItemType Directory
Set-Location -Path $githubPath
& git clone -b shkr/crankreturns https://github.com/Azure/azure-functions-host.git
Set-Location -Path azure-functions-host

$originalLocation = Get-Location
# Publish dotnet function apps.
$benchmarkAppsPath = "$githubPath\azure-functions-host\tools\Crank\BenchmarkApps\Dotnet";
$tempDirectory = 'C:\temp\BenchmarkApps'
$publishOutputRootDirectory = 'C:\FunctionApps'
New-Item -Path $publishOutputRootDirectory -ItemType Directory -Force

# copy the apps to temp directory for publishing so that the host global.json .NET SDK version doesn't interfere with the publish.
Copy-Item -Path $benchmarkAppsPath -Destination $tempDirectory -Recurse

$directories = Get-ChildItem -Path $tempDirectory -Directory

# Define the log file path
$logFilePath = "$publishOutputRootDirectory\publish.log"

# Function to write log messages to a file
function Write-Log {
    param (
        [string]$message
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "$timestamp - $message"
    Add-Content -Path $logFilePath -Value $logMessage
}

Write-Log "Child directory count inside $benchmarkAppsPath : $($directories.Count)"

# Loop through each directory and publish the app
foreach ($dir in $directories) {
    $appName = $dir.Name
    Write-Log "Processing $appName"

    try {
        # Find the .csproj or .sln file within the directory
        $projectFile = Get-ChildItem -Path $dir.FullName -Filter *.csproj -Recurse -File | Select-Object -First 1
        if (-not $projectFile) {
            Write-Log "No project file (.csproj) found in $appName"
            Write-Host "No project file (.csproj) found in $appName"
            continue
        }

        $publishOutputDir = Join-Path -Path $publishOutputRootDirectory -ChildPath "$appName/site/wwwroot"
        Write-Host "Publishing $($projectFile.FullName) to $publishOutputDir"
        Write-Log "Publishing $($projectFile.FullName) to $publishOutputDir"

        Set-Location -Path (Split-Path -Path $projectFile.FullName -Parent)
        # Publish the app
        dotnet publish -c Release -o $publishOutputDir

        Write-Log "Successfully published $appName"
    }
    catch {
        Write-Log "Failed to publish $appName. Error: $_"
        Write-Host "Failed to publish $appName. Error: $_"
    }
}
Set-Location -Path $originalLocation

# Setup Crank agent
$plaintextPassword = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($WindowsLocalAdminPasswordBase64))

psexec -accepteula -h -u $WindowsLocalAdminUserName -p $plaintextPassword `
    pwsh.exe -NoProfile -NonInteractive `
    -File "$githubPath\azure-functions-host\tools\Crank\Agent\setup-crank-agent-raw.ps1" `
    -ParametersJsonBase64 $ParametersJsonBase64 `
    -WindowsLocalAdminUserName $WindowsLocalAdminUserName `
    -WindowsLocalAdminPasswordBase64 $WindowsLocalAdminPasswordBase64 `
    -Verbose

if (-not $?) {
    throw "psexec exit code: $LASTEXITCODE"
}
