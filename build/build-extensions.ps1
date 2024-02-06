param (
  [string]$buildNumber = "0",    
  [string]$suffix = "",
  [ValidateSet("6", "8", "10")][string]$minorVersionPrefix = "10",
  [string]$hashesForHardlinksFile = "hashesForHardlinks.txt"
)

$rootDir = Split-Path -Parent $PSScriptRoot
$buildOutput = Join-Path $rootDir "buildoutput"

Import-Module $PSScriptRoot\Get-AzureFunctionsVersion -Force
$extensionVersion = Get-AzureFunctionsVersion $buildNumber $suffix $minorVersionPrefix
Write-Host "Site extension version: $extensionVersion"

# Construct variables for strings like "4.1.0-15898" and "4.1.0"
$versionParts = ($extensionVersion -Split "-")[0] -Split "\."
$majorMinorVersion = $versionParts[0] + "." + $versionParts[1]
$patchVersion = [int]$versionParts[2]
$isPatch = $patchVersion -gt 0
Write-Host "MajorMinorVersion is '$majorMinorVersion'. Patch version is '$patchVersion'. IsPatch: '$isPatch'"

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

    $cmd = "publish", "$PSScriptRoot\..\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj", "-r", "$targetRid", "--self-contained", "$isSelfContained", "-o", "$publishTarget", "-v", "m", "/p:BuildNumber=$buildNumber", "/p:IsPackable=false", "/p:MinorVersionPrefix=$minorVersionPrefix", "-c", "Release", $suffixCmd

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
    $officialSiteExtensionPath = "$siteExtensionPath\$extensionVersion"
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
        $destinationPath = "$patchedContentDirectory\$extensionVersion\$filePath"
        Write-Host "Copying $sourcePath to $destinationPath"
        $ValidPath = Test-Path "$destinationPath"

        If ($ValidPath -eq $False){
            New-Item -Path "$destinationPath" -Force > $null
        }

        Copy-Item "$sourcePath" "$destinationPath" -Force > $null

        # Get it into info
        $infoKeyValuePairs.Add("FileName", $key)
        $infoKeyValuePairs.Add("SourceVersion", $extensionVersion)
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
    $informationJson | ConvertTo-Json -depth 100 | Out-File "$patchedContentDirectory\$extensionVersion\HardlinksMetadata.json"

    # Zip it up
    ZipContent $patchedContentDirectory "$zipOutput\Functions.$extensionVersion.zip"

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
    
    #This needs to be removed post Ant 99 as it's a temporary workaround
    New-Item "$siteExtensionPath\$extensionVersion.hardlinksCreated" > $null
    
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
    if ($minorVersionPrefix -eq "10") {
        ZipContent $siteExtensionPath "$zipOutput\Functions.$extensionVersion$runtimeSuffix.zip"
    } elseif ($minorVersionPrefix -eq "8") {
        # The .NET 8 host doesn't require any workers. Doing this to save space.
        Write-Host "Removing workers."
        Remove-Item -Recurse -Force "$siteExtensionPath\$extensionVersion$runtimeSuffix\workers" -ErrorAction Stop
        # The host requires that this folder exists, even if it's empty
        New-Item -Itemtype directory -path $siteExtensionPath\$extensionVersion$runtimeSuffix\workers
        Write-Host
        ZipContent $siteExtensionPath "$zipOutput\FunctionsInProc8.$extensionVersion$runtimeSuffix.zip"
    } elseif ($minorVersionPrefix -eq "6") {
        ZipContent $siteExtensionPath "$zipOutput\FunctionsInProc.$extensionVersion$runtimeSuffix.zip"
    }

    # Create directory for content even if there is no patch build. This makes artifact uploading easier.
    $patchedContentDirectory = "$buildOutput\PatchedSiteExtension"
    New-Item -Itemtype directory -path $patchedContentDirectory -Force > $null

    # Construct patch
    if($isPatch)
    {
      Write-Host "======================================"
      Write-Host "Generating patch file"
      CreatePatchedSiteExtension $siteExtensionPath
      Write-Host "Done generating patch files"
      Write-Host "======================================"
      Write-Host
    }
    
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
