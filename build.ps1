param (
  [string]$buildNumber = "0",
  [string]$extensionVersion = "3.0.$buildNumber",
  [string]$suffix = ""
)

$currentDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildOutput = Join-Path $currentDir "buildoutput"
$hasSuffix = ![string]::IsNullOrEmpty($suffix)

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
    
    $cmd = "publish", ".\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj", "-r", "$targetRid", "--self-contained", "$isSelfContained", "/p:PublishReadyToRun=true", "/p:PublishReadyToRunEmitSymbols=true", "-o", "$publishTarget", "-v", "m", "/p:BuildNumber=$buildNumber", "/p:IsPackable=false", "/p:VersionSuffix=$suffix", "-c", "Release"

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
    Get-ChildItem "$rootPath\workers\powershell\runtimes" -Exclude $keepRuntimes -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    
    Write-Host "  Current size: $(GetFolderSizeInMb $rootPath) Mb"
}

function CreateSiteExtensions() {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $siteExtensionPath = "$buildOutput\temp_extension"
    
    Write-Host "======================================"
    Write-Host "Copying build to temp directory to prepare for zipping official site extension."
    Copy-Item -Path $buildOutput\publish\win-x86\ -Destination $siteExtensionPath\32bit -Force -Recurse > $null
    Copy-Item -Path $buildOutput\publish\win-x64 -Destination $siteExtensionPath\64bit -Force -Recurse > $null
    Copy-Item -Path $siteExtensionPath\32bit\applicationHost.xdt -Destination $siteExtensionPath -Force > $null
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