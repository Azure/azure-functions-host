param (
  [string]$buildNumber = "0",
  [string]$suffix = "",
  [ValidateSet("6", "8", "")][string]$minorVersionPrefix = "",
  [string]$hashesForHardlinksFile = "hashesForHardlinks.txt"
)

Import-Module "$PSScriptRoot\Get-AzureFunctionsVersion.psm1" -Force
$rootDir = Split-Path -Parent $PSScriptRoot
$outDir = "$rootDir\out"
$publishDir = "$outDir\pub\WebJobs.Script.WebHost"

$extensionVersion = Get-AzureFunctionsVersion $buildNumber $suffix $minorVersionPrefix
Write-Host "Site extension version: $extensionVersion"

#  Based on the minorVersionPrefix value, set target framework to be used for building/publishing
if ($minorVersionPrefix -eq "8") {
    $targetFramework = "net8.0"
} else {
    $targetFramework = "net6.0"
}

function ZipContent([string] $sourceDirectory, [string] $target) {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    Write-Host "======================================"
    Write-Host "Zipping $sourceDirectory into $target"

    if (Test-Path $target) {
      Remove-Item $target
    }

    Add-Type -assembly "system.io.compression.filesystem"
    [IO.Compression.ZipFile]::CreateFromDirectory($sourceDirectory, $target)

    Write-Host "Done zipping $target. Elapsed: $($stopwatch.Elapsed)"
    Write-Host "======================================"
    Write-Host ""
}

function BuildRuntime([string] $targetRid, [bool] $isSelfContained) {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    $publishTarget = "$publishDir\release_$targetFramework" + "_$targetRid"

    $projectPath = "$rootDir\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj"
    if (-not (Test-Path $projectPath))
    {
        throw "Project path '$projectPath' does not exist."
    }

    $cmd = "publish", $projectPath , "-r", "$targetRid", "-f", "$targetFramework", "--self-contained", "$isSelfContained", "-v", "m", "-c", "Release", "-p:IsPackable=false", "-p:BuildNumber=$buildNumber", "-p:MinorVersionPrefix=$minorVersionPrefix"

    Write-Host "======================================"
    Write-Host "Building $targetRid for target framework $targetFramework"
    Write-Host "  Self-Contained:    $isSelfContained"
    Write-Host "  Publish Directory:  $publishTarget"
    Write-Host ""
    Write-Host "dotnet $cmd"
    Write-Host ""

    & dotnet $cmd

    if ($LASTEXITCODE -ne 0)
    {
      exit $LASTEXITCODE
    }

    Write-Host ""
    $symbols = Get-ChildItem -Path $publishTarget -Filter *.pdb
    $symbols += Get-ChildItem -Path "$publishTarget\workers\dotnet-isolated\*" -Include "*.pdb", "*.dbg" -Recurse
    Write-Host "Zipping symbols: $($symbols.Count) symbols found"

    $symbolsPath = "$publishDir\Symbols"
    if (!(Test-Path -PathType Container $symbolsPath)) {
        New-Item -ItemType Directory -Path $symbolsPath | Out-Null
    }

    $symbols | Compress-Archive -DestinationPath "$symbolsPath\Functions.Symbols.$extensionVersion.$targetRid.zip" | Out-Null
    $symbols | Remove-Item | Out-Null

    Write-Host ""
    CleanOutput $publishTarget
    Write-Host ""
    Write-Host "Done building $targetRid for target framework $targetFramework. Elapsed: $($stopwatch.Elapsed)"
    Write-Host "======================================"
    Write-Host ""
}

function GetFolderSizeInMb([string] $rootPath) {
  return [math]::Round((Get-ChildItem $rootPath -Recurse | Measure-Object -Property Length -Sum -ErrorAction Stop).Sum / 1Mb, 2)
}

function CleanOutput([string] $rootPath) {
    Write-Host "Cleaning build output under $rootPath"
    Write-Host "  Current size: $(GetFolderSizeInMb $rootPath) Mb"

    Write-Host "  Removing any linux and osx runtimes"
    Remove-Item -Recurse -Force "$privateSiteExtensionPath\$bitness\runtimes\linux" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "$privateSiteExtensionPath\$bitness\runtimes\osx" -ErrorAction SilentlyContinue

    Write-Host "  Removing python worker"
    Remove-Item -Recurse -Force "$rootPath\workers\python" -ErrorAction SilentlyContinue

    $keepRuntimes = @('win', 'win-x86', 'win10-x86', 'win-x64', 'win10-x64')
    Write-Host "  Removing all powershell runtimes except $keepRuntimes"
    Get-ChildItem "$rootPath\workers\powershell" -Directory -ErrorAction SilentlyContinue |
      ForEach-Object { Get-ChildItem "$($_.FullName)\runtimes" -Directory -Exclude $keepRuntimes } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host "  Removing FunctionsNetHost(linux executable) and dependencies from dotnet-isolated worker"
    $dotnetIsolatedBinPath = Join-Path $rootPath "workers\dotnet-isolated\bin"
    if (Test-Path $dotnetIsolatedBinPath) {
        Remove-Item -Path (Join-Path $dotnetIsolatedBinPath "FunctionsNetHost") -ErrorAction SilentlyContinue
        Get-ChildItem -Path $dotnetIsolatedBinPath -Filter "*.so" | Remove-Item -ErrorAction SilentlyContinue
    }

    Write-Host "  Current size: $(GetFolderSizeInMb $rootPath) Mb"
}

function CreateSiteExtensions() {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $siteExtensionPath = "$publishDir\temp_extension"

    if (Test-Path $siteExtensionPath) {
        Write-Host "  Existing site extension path found. Deleting."
        Remove-Item $siteExtensionPath -Recurse -Force | Out-Null
    }

    # The official site extension needs to be nested inside a folder with its version.
    # Not using the suffix (eg: '-ci') here as it may not work correctly in a private stamp
    $officialSiteExtensionPath = "$siteExtensionPath\$extensionVersion"
    
    Write-Host "======================================"
    Write-Host "Copying build to temp directory to prepare for zipping official site extension."
    Copy-Item -Path "$publishDir\release_$targetFramework`_win-x86\" -Destination "$officialSiteExtensionPath\32bit" -Force -Recurse > $null
    Copy-Item -Path "$publishDir\release_$targetFramework`_win-x64\" -Destination "$officialSiteExtensionPath\64bit" -Force -Recurse > $null
    Copy-Item -Path $officialSiteExtensionPath\32bit\applicationHost.xdt -Destination $officialSiteExtensionPath -Force > $null
    Write-Host "  Deleting workers directory: $officialSiteExtensionPath\32bit\workers" 
    Remove-Item -Recurse -Force "$officialSiteExtensionPath\32bit\workers" -ErrorAction SilentlyContinue
    Write-Host "  Moving workers directory: $officialSiteExtensionPath\64bit\workers to $officialSiteExtensionPath\workers"
    Move-Item -Path "$officialSiteExtensionPath\64bit\workers" -Destination "$officialSiteExtensionPath\workers" 
     
    # This goes in the root dir
    Copy-Item $rootDir\src\WebJobs.Script.WebHost\extension.xml $siteExtensionPath > $null
    
    Write-Host "Done copying. Elapsed: $($stopwatch.Elapsed)"
    Write-Host "======================================"
    Write-Host ""

    Write-Host "======================================"
    Write-Host "Generating hashes for hard links"
    WriteHashesFile $siteExtensionPath/$extensionVersion
    Write-Host "Done generating hashes for hard links into $siteExtensionPath/$extensionVersion"
    Write-Host "======================================"
    Write-Host

    $zipOutput = "$publishDir\SiteExtension"
    $hashesForHardLinksPath = "$siteExtensionPath\$extensionVersion\$hashesForHardlinksFile"
    New-Item -Itemtype directory -path $zipOutput -Force > $null
    if ($minorVersionPrefix -eq "") {
        ZipContent $siteExtensionPath "$zipOutput\Functions.$extensionVersion.zip"
    } elseif ($minorVersionPrefix -eq "8") {
        Write-Host "======================================"
        # Only the "Functions" site extension supports hard links
        Write-Host "MinorVersionPrefix is '8'. Removing $hashesForHardLinksPath before zipping."
        Remove-Item -Force "$hashesForHardLinksPath" -ErrorAction Stop
        Write-Host "======================================"
        Write-Host
        ZipContent $siteExtensionPath "$zipOutput\FunctionsInProc8.$extensionVersion.zip"
    } elseif ($minorVersionPrefix -eq "6") {
        # Only the "Functions" site extension supports hard links
        Write-Host "======================================"
        Write-Host "MinorVersionPrefix is '6'. Removing $hashesForHardLinksPath before zipping."
        Remove-Item -Force "$hashesForHardLinksPath" -ErrorAction Stop
        Write-Host "======================================"
        Write-Host
        ZipContent $siteExtensionPath "$zipOutput\FunctionsInProc.$extensionVersion.zip"
    }

    Remove-Item $siteExtensionPath -Recurse -Force > $null

    Write-Host "======================================"
    $stopwatch.Reset()
    Write-Host "Copying build to temp directory to prepare for zipping private site extension."
    Copy-Item -Path $publishDir\release_$targetFramework`_win-x86\ -Destination $siteExtensionPath\SiteExtensions\Functions\32bit -Force -Recurse > $null
    Copy-Item -Path $siteExtensionPath\SiteExtensions\Functions\32bit\applicationHost.xdt -Destination $siteExtensionPath\SiteExtensions\Functions -Force > $null
    Write-Host "Done copying. Elapsed: $($stopwatch.Elapsed)"
    Write-Host "======================================"
    Write-Host ""

    $zipOutput = "$publishDir\PrivateSiteExtension"
    New-Item -Itemtype directory -path $zipOutput -Force > $null
    ZipContent $siteExtensionPath "$zipOutput\Functions.Private.$extensionVersion.win-x32.inproc.zip"
    
    Remove-Item $siteExtensionPath -Recurse -Force > $null
}

function WriteHashesFile([string] $directoryPath) {
  New-Item -Path "$directoryPath/../temp_hashes" -ItemType Directory | Out-Null
  $temp_current = (Get-Location)
  Set-Location $directoryPath
  Get-ChildItem -Recurse $directoryPath | Where-Object { $_.PsIsContainer -eq $false } | Foreach-Object { "Hash:" + [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((Get-FileHash -Algorithm MD5 $_.FullName).Hash)) + " FileName:" + (Resolve-Path -Relative -Path $_.FullName) } | Out-File -FilePath "$directoryPath\..\temp_hashes\$hashesForHardlinksFile"
  Move-Item -Path "$directoryPath/../temp_hashes/$hashesForHardlinksFile" -Destination "$directoryPath" -Force
  Set-Location $temp_current
  Remove-Item "$directoryPath/../temp_hashes" -Recurse -Force > $null
}

Write-Host "Output directory: $publishDir"
if (Test-Path $publishDir) {
    Write-Host "  Existing build output found. Deleting."
    Remove-Item $publishDir -Recurse -Force -ErrorAction Stop
}

Write-Host "Extensions version: $extensionVersion"
Write-Host ""

BuildRuntime "win-x86"
BuildRuntime "win-x64"

CreateSiteExtensions
