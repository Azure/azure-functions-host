param (
  [string]$buildNumber = "0",  
  [string]$suffix = "",  
  [string]$hashesForHardlinksFile = "hashesForHardlinks.txt"
)

$rootDir = Split-Path -Parent $PSScriptRoot
$buildOutput = Join-Path $rootDir "buildoutput"
$hasSuffix = ![string]::IsNullOrEmpty($suffix)

$suffixCmd = ""
if ($hasSuffix) {
  $suffixCmd = "/p:VersionSuffix=$suffix"
}

# use the same logic as the projects to generate the site extension version
$cmd = "build", "$rootDir\build\common.props", "/t:GenerateSiteExtensionVersion", "-restore:False", "-o", "$buildOutput", "/p:BuildNumber=$buildNumber", $suffixCmd, "-v", "q", "--nologo", "-clp:NoSummary"
& dotnet $cmd

$versionTxt = "$rootDir\buildoutput\version.txt"
$extensionVersion = Get-Content $versionTxt -First 1

Write-Host "Found site extension version in '$versionTxt': $extensionVersion"

function ZipContent([string] $sourceDirectory, [string] $target)
{
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
    $runtimeSuffix = ".$targetRid"
    $ridSwitch = ""
    
    $publishTarget = "$buildOutput\publish\$targetRid"
    $symbolsTarget = "$buildOutput\symbols\$targetRid"

    $projectPath = "$PSScriptRoot\..\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj"
    if (-not (Test-Path $projectPath))
    {
        throw "Project path '$projectPath' does not exist."
    }

    $cmd = "publish", "$PSScriptRoot\..\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj", "-r", "$targetRid", "--self-contained", "$isSelfContained", "-o", "$publishTarget", "-v", "m", "/p:BuildNumber=$buildNumber", "/p:IsPackable=false", "-c", "Release", $suffixCmd

    Write-Host "======================================"
    Write-Host "Building $targetRid"
    Write-Host "  Self-Contained:    $isSelfContained"
    Write-Host "  Output Directory:  $publishTarget"
    Write-Host "  Symbols Directory: $symbolsTarget"
    Write-Host ""
    Write-Host "dotnet $cmd"
    Write-Host ""
    
    & dotnet $cmd

    if ($LASTEXITCODE -ne 0)
    {
      exit $LASTEXITCODE
    }

    Write-Host ""
    Write-Host "Moving symbols to $symbolsTarget"
    New-Item -Itemtype directory -path $symbolsTarget -Force > $null
    Move-Item -Path $publishTarget\*.pdb -Destination $symbolsTarget -Force > $null
    Write-Host ""    
    CleanOutput "$publishTarget"        
    Write-Host ""
    Write-Host "Done building $targetRid. Elapsed: $($stopwatch.Elapsed)"
    Write-Host "======================================"
    Write-Host ""

    $zipOutput = "$buildOutput\Symbols"
    New-Item -Itemtype directory -path $zipOutput -Force > $null

    ZipContent $symbolsTarget "$zipOutput\Functions.Symbols.$extensionVersion$runtimeSuffix.zip"
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

    Write-Host "  Removing non-win32 node grpc binaries"
    Get-ChildItem "$rootPath\workers\node\grpc\src\node\extension_binary" -ErrorAction SilentlyContinue | 
    Foreach-Object {
        if (-Not ($_.FullName -Match "win32")) {
            Remove-Item -Recurse -Force $_.FullName
        }
    }

    $keepRuntimes = @('win', 'win-x86', 'win10-x86', 'win-x64', 'win10-x64')
    Write-Host "  Removing all powershell runtimes except $keepRuntimes"
    Get-ChildItem "$rootPath\workers\powershell" -Directory -ErrorAction SilentlyContinue |
      ForEach-Object { Get-ChildItem "$($_.FullName)\runtimes" -Directory -Exclude $keepRuntimes } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    
    Write-Host "  Current size: $(GetFolderSizeInMb $rootPath) Mb"
}

function CreateSiteExtensions() {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $siteExtensionPath = "$buildOutput\temp_extension"

    # The official site extension needs to be nested inside a folder with its version.
    # Not using the suffix (eg: '-ci') here as it may not work correctly in a private stamp
    $officialSiteExtensionPath = "$siteExtensionPath\$extensionVersion"
    
    Write-Host "======================================"
    Write-Host "Copying build to temp directory to prepare for zipping official site extension."
    Copy-Item -Path $buildOutput\publish\win-x86\ -Destination $officialSiteExtensionPath\32bit -Force -Recurse > $null
    Copy-Item -Path $buildOutput\publish\win-x64 -Destination $officialSiteExtensionPath\64bit -Force -Recurse > $null
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
    
    $zipOutput = "$buildOutput\SiteExtension"
    New-Item -Itemtype directory -path $zipOutput -Force > $null
    ZipContent $siteExtensionPath "$zipOutput\Functions.$extensionVersion$runtimeSuffix.zip"
    
    Remove-Item $siteExtensionPath -Recurse -Force > $null    
    
    Write-Host "======================================"
    $stopwatch.Reset()
    Write-Host "Copying build to temp directory to prepare for zipping private site extension."
    Copy-Item -Path $buildOutput\publish\win-x86\ -Destination $siteExtensionPath\SiteExtensions\Functions\32bit -Force -Recurse > $null
    Copy-Item -Path $siteExtensionPath\SiteExtensions\Functions\32bit\applicationHost.xdt -Destination $siteExtensionPath\SiteExtensions\Functions -Force > $null
    Write-Host "Done copying. Elapsed: $($stopwatch.Elapsed)"
    Write-Host "======================================"
    Write-Host ""
    
    $zipOutput = "$buildOutput\PrivateSiteExtension"
    New-Item -Itemtype directory -path $zipOutput -Force > $null
    ZipContent $siteExtensionPath "$zipOutput\Functions.Private.$extensionVersion.win-x32.inproc.zip"
    
    Remove-Item $siteExtensionPath -Recurse -Force > $null
}

function WriteHashesFile([string] $directoryPath) {
  New-Item -Path "$directoryPath/../temp_hashes" -ItemType Directory | Out-Null
  $temp_current = (Get-Location)
  Set-Location $directoryPath
  Get-ChildItem -Recurse $directoryPath | where { $_.PsIsContainer -eq $false } | Foreach-Object { "Hash:" + [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((Get-FileHash -Algorithm MD5 $_.FullName).Hash)) + " FileName:" + (Resolve-Path -Relative -Path $_.FullName) } | Out-File -FilePath "$directoryPath\..\temp_hashes\$hashesForHardlinksFile"
  Move-Item -Path "$directoryPath/../temp_hashes/$hashesForHardlinksFile" -Destination "$directoryPath" -Force
  Set-Location $temp_current
  Remove-Item "$directoryPath/../temp_hashes" -Recurse -Force > $null
}

Write-Host
dotnet --info
Write-Host
Write-Host "Output directory: $buildOutput"
if (Test-Path $buildOutput) {
    Write-Host "  Existing build output found. Deleting."
    Remove-Item $buildOutput -Recurse -Force
}
Write-Host "Extensions version: $extensionVersion"
Write-Host ""

BuildRuntime "win-x86"
BuildRuntime "win-x64"

CreateSiteExtensions