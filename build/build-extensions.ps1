param (
  [string]$buildNumber = "0",
  [string]$extensionVersion = "3.0.$buildNumber",
  [string]$suffix = "",
  [string]$commitHash = "N/A"
)

$currentDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $currentDir
$buildOutput = Join-Path $rootDir "buildoutput"
$hasSuffix = ![string]::IsNullOrEmpty($suffix)

$extensionVersionNoSuffix = $extensionVersion

if ($hasSuffix) {
  $extensionVersion = "$extensionVersion-$suffix"
}

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
    
    if ($isSelfContained) {
        $publishTarget = "$publishTarget.self-contained"
        $symbolsTarget = "$symbolsTarget.self-contained"
    }

    $suffixCmd = ""
    if ($hasSuffix) {
      $suffixCmd = "/p:VersionSuffix=$suffix"
    }

    $cmd = "publish", ".\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj", "-r", "$targetRid", "--self-contained", "$isSelfContained", "/p:PublishReadyToRun=true", "/p:PublishReadyToRunEmitSymbols=true", "-o", "$publishTarget", "-v", "m", "/p:BuildNumber=$buildNumber", "/p:IsPackable=false", "/p:CommitHash=$commitHash", "-c", "Release", $suffixCmd

    Write-Host "======================================"
    Write-Host "Building $targetRid"
    Write-Host "  Self-Contained:    $isSelfContained"
    Write-Host "  Output Directory:  $publishTarget"
    Write-Host "  Symbols Directory: $symbolsTarget"
    Write-Host ""
    Write-Host "dotnet $cmd"
    Write-Host ""
    
    & dotnet $cmd

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

    ZipContent $symbolsTarget "$buildOutput\Functions.Symbols.$extensionVersion$runtimeSuffix.zip"
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
    $officialSiteExtensionPath = "$siteExtensionPath\$extensionVersionNoSuffix"
    
    Write-Host "======================================"
    Write-Host "Copying build to temp directory to prepare for zipping official site extension."
    Copy-Item -Path $buildOutput\publish\win-x86\ -Destination $officialSiteExtensionPath\32bit -Force -Recurse > $null
    Copy-Item -Path $buildOutput\publish\win-x64 -Destination $officialSiteExtensionPath\64bit -Force -Recurse > $null
    Copy-Item -Path $officialSiteExtensionPath\32bit\applicationHost.xdt -Destination $officialSiteExtensionPath -Force > $null

    Write-Host "======================================"
    Write-Host "Deleting workers directory: $officialSiteExtensionPath\32bit\workers" 
    Remove-Item -Recurse -Force "$officialSiteExtensionPath\32bit\workers" -ErrorAction SilentlyContinue
    Write-Host "Moving workers directory:$officialSiteExtensionPath\64bit\workers to" $privateSiteExtensionPath 
    Move-Item -Path "$officialSiteExtensionPath\64bit\workers"  -Destination "$officialSiteExtensionPath\workers" 
     
    # This goes in the root dir
    Copy-Item $rootDir\src\WebJobs.Script.WebHost\extension.xml $siteExtensionPath > $null
    
    Write-Host "Done copying. Elapsed: $($stopwatch.Elapsed)"
    Write-Host "======================================"
    Write-Host ""

    ZipContent $siteExtensionPath "$buildOutput\Functions.$extensionVersion$runtimeSuffix.zip"
    
    Remove-Item $siteExtensionPath -Recurse -Force > $null
    
    Write-Host "======================================"
    Write-Host "Copying build to temp directory to prepare for zipping private site extension."
    Copy-Item -Path $buildOutput\publish\win-x86\ -Destination $siteExtensionPath\SiteExtensions\Functions\32bit -Force -Recurse > $null
    Copy-Item -Path $siteExtensionPath\SiteExtensions\Functions\32bit\applicationHost.xdt -Destination $siteExtensionPath\SiteExtensions\Functions -Force > $null
    Write-Host "Done copying. Elapsed: $($stopwatch.Elapsed)"
    Write-Host "======================================"
    Write-Host ""
    
    ZipContent $siteExtensionPath "$buildOutput\Functions.Private.$extensionVersion.win-x32.inproc.zip"
    
    Remove-Item $siteExtensionPath -Recurse -Force > $null
}

Write-Host ""
dotnet --version
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