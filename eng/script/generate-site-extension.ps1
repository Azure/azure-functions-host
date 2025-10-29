param(
    [Parameter(Mandatory = $true)]
    [string]$WorkerPackageName,
    [Parameter(Mandatory = $true)]
    [string]$WorkerVersion,
    [Parameter(Mandatory = $true)]
    [string]$OutputDir
)

# Download nuget.exe if not already present
$nugetExe = "nuget.exe"
if (-not (Get-Command $nugetExe -ErrorAction SilentlyContinue)) {
    Write-Host "Downloading nuget.exe..."
    Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nugetExe
    Write-Host "Download completed at location: $(Get-Location)"
}

# Create working directories
$packageDir = Join-Path $OutputDir "package"
$siteExtensionDir = Join-Path $OutputDir "content"
Remove-Item -Recurse -Force $packageDir, $siteExtensionDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $packageDir | Out-Null
New-Item -ItemType Directory -Path $siteExtensionDir | Out-Null

# Install the worker nuget package
& nuget.exe install $WorkerPackageName -Version $WorkerVersion -OutputDirectory $packageDir -ConfigFile nuget.config
$packageFile = Get-ChildItem -Path $packageDir -Recurse -Filter "*.nupkg" | Select-Object -First 1
if (-not $packageFile) {
    Write-Error "NuGet package not found after download."
    exit 1
}

Expand-Archive -Path $packageFile.FullName -DestinationPath $siteExtensionDir

$zipPath = Join-Path $OutputDir "$WorkerPackageName-$WorkerVersion.zip"
if (Test-Path $zipPath) { 
    Remove-Item $zipPath 
}

if ($WorkerPackageName -eq "Microsoft.Azure.Functions.PythonWorker") 
{
    $pythonPath = Join-Path "$siteExtensionDir" "python"
    New-Item -ItemType Directory -Path $pythonPath | Out-Null

    Copy-Item -Path "$siteExtensionDir\tools\*" -Destination $pythonPath -Recurse -Force
    Compress-Archive -Path $pythonPath -DestinationPath $zipPath
} 
else 
{
    Compress-Archive -Path "$siteExtensionDir\contentFiles\any\any\workers\*" -DestinationPath $zipPath
}

Write-Host "Site extension package created at: $zipPath"
Remove-Item -Recurse -Force $packageDir, $siteExtensionDir -ErrorAction SilentlyContinue
