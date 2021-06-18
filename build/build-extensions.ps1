param (
  [string]$buildNumber,
  [string]$majorMinorVersion,
  [string]$patchVersion,
  [string]$v2CompatibleExtensionVersion = "2.1.0",  
  [string]$suffix = "",
  [string]$commitHash = "N/A",
  [string]$hashesForHardlinksFile = "hashesForHardlinks.txt"
)

$extensionVersion = "$majorMinorVersion.$patchVersion"
Write-Host "ExtensionVersion is $extensionVersion"
Write-Host "BuildNumber is $buildNumber"

$rootDir = Split-Path -Parent $PSScriptRoot
$buildOutput = Join-Path $rootDir "buildoutput"
$hasSuffix = ![string]::IsNullOrEmpty($suffix)

if(![string]::IsNullOrEmpty($buildNumber)) {
  $v2CompatibleExtensionVersion = "2.1.$buildNumber"
}

$extensionVersionNoSuffix = $extensionVersion
$v2CompatibleExtensionVersionNoSuffix = $v2CompatibleExtensionVersion

if ($hasSuffix) {
  $extensionVersion = "$extensionVersion-$suffix"  
  $v2CompatibleExtensionVersion = "$v2CompatibleExtensionVersion-$suffix"
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

    $suffixCmd = ""
    if ($hasSuffix) {
      $suffixCmd = "/p:VersionSuffix=$suffix"
    }

    $projectPath = "$PSScriptRoot\..\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj"
    if (-not (Test-Path $projectPath))
    {
        throw "Project path '$projectPath' does not exist."
    }

    $cmd = "publish", "$PSScriptRoot\..\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj", "-r", "$targetRid", "--self-contained", "$isSelfContained", "/p:PublishReadyToRun=true", "/p:PublishReadyToRunEmitSymbols=true", "-o", "$publishTarget", "-v", "m", "/p:BuildNumber=$buildNumber", "/p:IsPackable=false", "/p:CommitHash=$commitHash", "-c", "Release", $suffixCmd

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

function CreatePatchedSiteExtension([string] $siteExtensionPath) {
  try {
    Write-Host "SiteExtensionPath is $siteExtensionPath"
    $officialSiteExtensionPath = "$siteExtensionPath\$extensionVersionNoSuffix"
    $baseVersion = "$majorMinorVersion.0"
    $baseZipPath = "$buildOutput\BaseZipDirectory"
    $baseExtractedPath = "$buildOutput\BaseZipDirectory\Extracted"
    
    # Try to download base version
    New-Item -Itemtype "directory" -path "$baseZipPath" -Force > $null
    New-Item -Itemtype "directory" -path "$baseExtractedPath" -Force > $null
    $baseZipUrl = "https://github.com/Azure/azure-functions-host/releases/download/v$majorMinorVersion.0/Functions.$majorMinorVersion.0.zip"

    Write-Host "Downloading from $baseZipUrl"
    (New-Object System.Net.WebClient).DownloadFile($baseZipUrl, "$baseZipPath\Functions.$majorMinorVersion.0.zip")
    Write-Host "Download complete"

    # Extract zip
    Expand-Archive -LiteralPath "$baseZipPath\Functions.$majorMinorVersion.0.zip" -DestinationPath "$baseExtractedPath"
    
    # Create directory for patch
    $zipOutput = "$buildOutput\ZippedPatchSiteExtension"
    New-Item -Itemtype directory -path $zipOutput -Force > $null

    # Create directory for content
    $patchedContentDirectory = "$buildOutput\PatchedSiteExtension"
    New-Item -Itemtype directory -path $patchedContentDirectory -Force > $null

    # Copy extensions.xml as is
    Copy-Item "$siteExtensionPath\extension.xml" -Destination "$patchedContentDirectory\extension.xml"

    # Read hashes.txt for base
    $hashForBase = @{}
    foreach($line in Get-Content "$baseExtractedPath\$baseVersion\hashesForHardlinks.txt") {
      $lineContents = $line.Split(" ")
      $hashKey = $lineContents[1].Split(":")[1]
      $hashValue = $lineContents[0].Split(":")[1]
  
      $hashForBase.Add($hashKey, $hashValue)
    }

    # Read hashes.txt for patched
    $hashForPatched = @{}
    foreach($line in Get-Content "$officialSiteExtensionPath\hashesForHardlinks.txt") {
      $lineContents = $line.Split(" ")
      $hashKey = $lineContents[1].Split(":")[1]
      $hashValue = $lineContents[0].Split(":")[1]
  
      $hashForPatched.Add($hashKey, $hashValue)
    }

    # Iterate over patched to generate the appropriate set of files
    $informationJson = New-Object System.Collections.ArrayList
    foreach($key in $hashForPatched.Keys) {
      $infoKeyValuePairs = @{}
      
      # If key doesn't exist in base, or if the keys exists but their hashes don't match, copy over.
      if((!$hashForBase.ContainsKey($key)) -or ($hashForPatched[$key] -ne $hashForBase[$key])) {
        $filePath = $key.Replace(".\","")
        $sourcePath = "$officialSiteExtensionPath\$filePath"
        $destinationPath = "$patchedContentDirectory\$extensionVersionNoSuffix\$filePath"
        Write-Host "Copying $sourcePath to $destinationPath"
        $ValidPath = Test-Path "$destinationPath"

        If ($ValidPath -eq $False){
            New-Item -Path "$destinationPath" -Force > $null
        }

        Copy-Item "$sourcePath" "$destinationPath" -Force > $null

        # Get it into info
        $infoKeyValuePairs.Add("FileName", $key)
        $infoKeyValuePairs.Add("SourceVersion", $extensionVersionNoSuffix)
        $infoKeyValuePairs.Add("HashValue", $hashForPatched[$key])
        $informationJson.Add($infoKeyValuePairs)
        continue
      }

      # Add info that would help get this file from base
      $infoKeyValuePairs.Add("FileName", $key)
      $infoKeyValuePairs.Add("SourceVersion", $baseVersion)
      $infoKeyValuePairs.Add("HashValue", $hashForBase[$key])
      $informationJson.Add($infoKeyValuePairs)
    }
    $informationJson | ConvertTo-Json -depth 100 | Out-File "$patchedContentDirectory\$extensionVersionNoSuffix\HardlinksMetadata.json"

    # Zip it up
    ZipContent $patchedContentDirectory "$zipOutput\Functions.$extensionVersion$runtimeSuffix.zip"

    # Clean up
    Remove-Item $patchedContentDirectory -Recurse -Force > $null
  }
  catch {
    Write-Host $_.Exception
    $statusCode = $_.Exception.Response.StatusCode.Value__
    Write-Host "Invoking url $baseZipUrl returned status code of $statusCode which could mean that no base version exists. The full version needs to be deployed"
  }
}

function CreateSiteExtensions() {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $siteExtensionPath = "$buildOutput\temp_extension"
    $v2CompatibleSiteExtensionPath = "$buildOutput\temp_extension_v2"

    # The official site extension needs to be nested inside a folder with its version.
    # Not using the suffix (eg: '-ci') here as it may not work correctly in a private stamp
    $officialSiteExtensionPath = "$siteExtensionPath\$extensionVersionNoSuffix"
    $officialV2CompatibleSiteExtensionPath = "$v2CompatibleSiteExtensionPath\$v2CompatibleExtensionVersionNoSuffix"
    
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
    WriteHashesFile $siteExtensionPath/$extensionVersionNoSuffix
    Write-Host "Done generating hashes for hard links into $siteExtensionPath/$extensionVersionNoSuffix"
    Write-Host "======================================"
    Write-Host
    
    Write-Host "======================================"
    $stopwatch.Reset()
    Write-Host "Copying $extensionVersion site extension to generate $v2CompatibleExtensionVersion."
    Copy-Item -Path $officialSiteExtensionPath -Destination $officialV2CompatibleSiteExtensionPath\$v2CompatibleExtensionVersionNoSuffix -Force -Recurse > $null
    Copy-Item $rootDir\src\WebJobs.Script.WebHost\extension.xml $officialV2CompatibleSiteExtensionPath > $null
    Write-Host "Done copying. Elapsed: $($stopwatch.Elapsed)"
    Write-Host "======================================"
    Write-Host

    $zipOutput = "$buildOutput\V2SiteExtension"
    New-Item -Itemtype directory -path $zipOutput -Force > $null
    ZipContent $officialV2CompatibleSiteExtensionPath "$zipOutput\Functions.$v2CompatibleExtensionVersion$runtimeSuffix.zip"

    # This needs to be determined if it's patch or not.
    $zipOutput = "$buildOutput\SiteExtension"
    New-Item -Itemtype directory -path $zipOutput -Force > $null
    ZipContent $siteExtensionPath "$zipOutput\Functions.$extensionVersion$runtimeSuffix.zip"

    # Construct patch
    if(([int]$patchVersion) -gt 0)
    {
      Write-Host "======================================"
      Write-Host "Generating patch file"
      CreatePatchedSiteExtension $siteExtensionPath
      Write-Host "Done generating patch files"
      Write-Host "======================================"
      Write-Host
    }
    
    Remove-Item $siteExtensionPath -Recurse -Force > $null    
    Remove-Item $v2CompatibleSiteExtensionPath -Recurse -Force > $null
    
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

Write-Host ""
dotnet --version
Write-Host "Output directory: $buildOutput"
if (Test-Path $buildOutput) {
    Write-Host "  Existing build output found. Deleting."
    Remove-Item $buildOutput -Recurse -Force
}
Write-Host "Extensions version: $extensionVersion"
Write-Host "V2 compatible Extensions version: $v2CompatibleExtensionVersion"
Write-Host ""

BuildRuntime "win-x86"
BuildRuntime "win-x64"

CreateSiteExtensions
