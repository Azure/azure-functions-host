﻿param (
  [string]$buildNumber = "0",
  [string]$extensionVersion = "2.0.$buildNumber",
  [bool]$includeSuffix = $true
)

if ($includeSuffix)
{
    $extensionVersion += "-prerelease"
}

$currentDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildOutput = Join-Path $currentDir "buildoutput"

function ZipContent([string] $sourceDirectory, [string] $target)
{
  Write-Host $sourceDirectory
  Write-Host $target
  
  if (Test-Path $target) {
    Remove-Item $target
  }
  Add-Type -assembly "system.io.compression.filesystem"
  [IO.Compression.ZipFile]::CreateFromDirectory($sourceDirectory, $target)
}

function CrossGen([string] $runtime, [string] $publishTarget, [string] $privateSiteExtensionPath)
{
    Write-Host "publishTarget: " $publishTarget
    Write-Host "privateSiteExtensionPath: " $privateSiteExtensionPath

    $selfContained = Join-Path $publishTarget "self-contained"
    $crossGen = "$publishTarget\download\crossgen\crossgen.exe"
    $symbolsPath = Join-Path $publishTarget "Symbols"
    new-item -itemtype directory -path $symbolsPath

    #https://dotnet.myget.org/feed/dotnet-core/package/nuget/runtime.win-x86.Microsoft.NETCore.Jit
    DownloadNupkg "https://dotnet.myget.org/F/dotnet-core/api/v2/package/runtime.$runtime.Microsoft.NETCore.Jit/2.2.0-servicing-26820-03" @("runtimes\$runtime\native\clrjit.dll")  @("$publishTarget\download\clrjit")
    #https://dotnet.myget.org/feed/dotnet-core/package/nuget/runtime.win-x86.Microsoft.NETCore.Runtime.CoreCLR
    DownloadNupkg "https://dotnet.myget.org/F/dotnet-core/api/v2/package/runtime.$runtime.Microsoft.NETCore.Runtime.CoreCLR/2.2.0-servicing-26820-03"  @("tools\crossgen.exe")  @("$publishTarget\download\crossgen")

    # Publish self-contained app with all required dlls for crossgen
    dotnet publish .\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj -r $runtime -o "$selfContained" -v q /p:BuildNumber=$buildNumber    

    # Modify web.config for inproc
    dotnet tool install -g dotnet-xdt --version 2.1.0 2> $null
    dotnet-xdt -s "$privateSiteExtensionPath\web.config" -t "$privateSiteExtensionPath\web.InProcess.$runtime.xdt" -o "$privateSiteExtensionPath\web.config"

    $successfullDlls =@()
    $failedDlls = @()
    
    Get-ChildItem $privateSiteExtensionPath -Filter *.dll | 
    Foreach-Object {       
        $prm = "/JITPath", "$publishTarget\download\clrjit\clrjit.dll", "/Platform_Assemblies_Paths", "$selfContained", "/nologo", "/in", $_.FullName
        # output for Microsoft.Azure.WebJobs.Script.WebHost.dll is Microsoft.Azure.WebJobs.Script.WebHost.exe.dll by default
        if ($_.FullName -like "*Microsoft.Azure.WebJobs.Script.WebHost.dll") {
            $prm += "/out"        
            $prm += Join-Path $privateSiteExtensionPath "Microsoft.Azure.WebJobs.Script.WebHost.ni.dll"
        }

        & $crossGen $prm >> $buildOutput\crossgenout.$runtime.txt  2>&1

        $niDll = Join-Path $privateSiteExtensionPath $([io.path]::GetFileNameWithoutExtension($_.FullName) + ".ni.dll")
        if ([System.IO.File]::Exists($niDll)) {
            Remove-Item $_.FullName
            Rename-Item -Path $niDll -NewName $_.FullName

            & $crossGen "/Platform_Assemblies_Paths", "$selfContained", "/CreatePDB", "$symbolsPath", $_.FullName >> $buildOutput\crossgenout-PDBs.$runtime.txt 2>&1

            $successfullDlls+=[io.path]::GetFileName($_.FullName)
        } else {
            $failedDlls+=[io.path]::GetFileName($_.FullName)
        }                
    }
    
    # print results of crossgen process
    $successfullDllsCount = $successfullDlls.length
    $failedDllsCount = $failedDlls.length
    Write-Host "CrossGen($runtime) results: Successfull: $successfullDllsCount, Failed: $failedDllsCount"
    if ($failedDlls.length -gt 0) {
        Write-Host "Failed CrossGen dlls:"
        Write-Host $failedDlls
    }
    
    
    if ($runtime -eq "win-x86") {
        Copy-Item -Path $privateSiteExtensionPath\runtimes\win\native\*  -Destination $privateSiteExtensionPath -Force
    }

    #read-host "Press ENTER to continue..."
    Remove-Item -Recurse -Force $selfContained -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $publishTarget\download -ErrorAction SilentlyContinue
}

function AddDiaSymReaderToPath()
{
    $infoContent = dotnet --info
    $sdkBasePath = $infoContent  |
        Where-Object {$_ -match 'Base Path:'} |
        ForEach-Object {
            $_ -replace '\s+Base Path:',''
        }

    $diaSymPath = Join-Path $sdkBasePath.Trim() "Roslyn\bincore\runtimes\win\native"

    Write-Host "Adding DiaSymReader location to path ($diaSymPath)" -ForegroundColor Yellow
    $env:Path = "$diaSymPath;$env:Path"
}

function DownloadNupkg([string] $nupkgPath, [string[]]$from, [string[]]$to) {
    $tempFolderName = [System.IO.Path]::GetFileNameWithoutExtension([System.IO.Path]::GetTempFileName())
    $tempFolder = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath() ,$tempFolderName)
    [System.IO.Directory]::CreateDirectory($tempFolder)
    Remove-Item $tempFolder
    $tempFile = [System.IO.Path]::GetTempFileName() |
        Rename-Item -NewName { $_ -replace 'tmp$', 'zip' } -PassThru

    Write-Host "Downloading '$nupkgPath' to '$tempFile'"
    Invoke-WebRequest -Uri $nupkgPath -OutFile $tempFile
    Write-Host "Extracting from '$tempFile' to '$tempFolder'"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($tempFile, $tempFolder)
    
    #copy nupkg files
    for ($i=0; $i -lt $from.length; $i++) {
        New-Item $to -Type Directory -Force
        Copy-Item -Path $("$tempFolder\" + $from[$i]) -Destination $($to[$i] + "\") -Force -Verbose
    }
}

function BuildPackages([bool] $isNoRuntime) {
    if($isNoRuntime) {
        BuildOutput ""
        CreateZips ".no-runtime"
    } else {
        BuildOutput "win-x86"
        BuildOutput "win-x64"

        New-Item -Itemtype directory -path $buildOutput\publish.runtime\SiteExtensions\Functions
        Move-Item -Path $buildOutput\publish.win-x86\SiteExtensions\Functions -Destination $buildOutput\publish.runtime\SiteExtensions\Functions\32bit -Force
        Move-Item -Path $buildOutput\publish.win-x64\SiteExtensions\Functions -Destination $buildOutput\publish.runtime\SiteExtensions\Functions\64bit -Force
        Move-Item -Path $buildOutput\publish.runtime\SiteExtensions\Functions\32bit\applicationHost.xdt -Destination $buildOutput\publish.runtime\SiteExtensions\Functions -Force
        Remove-Item -Path $buildOutput\publish.runtime\SiteExtensions\Functions\64bit\applicationHost.xdt

        CreateZips ".runtime"
    }
}


function BuildOutput([string] $runtime) {
    $runtimeSuffix = ""
    if (![string]::IsNullOrEmpty($runtime)) {
        $runtimeSuffix = ".$runtime"
    } else {
        $runtimeSuffix = ".no-runtime"
    }

    $publishTarget = "$buildOutput\publish$runtimeSuffix"
    $siteExtensionPath = "$publishTarget\SiteExtensions"
    $privateSiteExtensionPath = "$siteExtensionPath\Functions"
    
    dotnet publish .\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj -o "$privateSiteExtensionPath" -v q /p:BuildNumber=$buildNumber /p:IsPackable=false -c Release

    # replace IL dlls with crossgen dlls
    if (![string]::IsNullOrEmpty($runtime)) {
        CrossGen $runtime $publishTarget $privateSiteExtensionPath
    }
}


function CreateZips([string] $runtimeSuffix) {
    $publishTarget = "$buildOutput\publish$runtimeSuffix"
    $siteExtensionPath = "$publishTarget\SiteExtensions"
    $privateSiteExtensionPath = "$siteExtensionPath\Functions"

    # We do not want .runtime as suffix for zips
    if ($runtimeSuffix -eq ".runtime") {
        $runtimesuffix = "";
    }

    ZipContent $privateSiteExtensionPath "$buildOutput\Functions.Binaries.$extensionVersion$runtimeSuffix.zip"

    if ($runtimeSuffix -eq  ".no-runtime") {
        # Project cleanup (trim some project files - this should be revisited)
        cleanExtension ""



        # Make the zip
        ZipContent $publishTarget "$buildOutput\Functions.Private.$extensionVersion$runtimeSuffix.zip"

    } else {
        # Project cleanup (trim some project files - this should be revisited)
        cleanExtension "32bit"
        cleanExtension "64bit"

        # Create private extension for internal usage. To minimize size remove 64bit folder.
        $tempPath = "$buildOutput\win-x32.inproc.temp\SiteExtensions"

        # Make a temp location
        New-Item -Itemtype directory -path $tempPath -ErrorAction SilentlyContinue
       
        # Copy all files to temp folder
        Copy-Item -Path $privateSiteExtensionPath -Destination $tempPath -Recurse

        # Delete x64 folder to reduce size
        Remove-Item "$tempPath\Functions\64bit" -Recurse

        # Make the zip
        ZipContent "$buildOutput\win-x32.inproc.temp" "$buildOutput\Functions.Private.$extensionVersion.win-x32.inproc.zip"

        Remove-Item $tempPath -Recurse
    }

    # Zip up symbols for builds with runtime embedded
    if ($runtimeSuffix -eq  "") {
        ZipContent "$buildOutput\publish.win-x86\Symbols" "$buildOutput\Functions.Symbols.$extensionVersion.win-x86.zip"
        ZipContent "$buildOutput\publish.win-x64\Symbols" "$buildOutput\Functions.Symbols.$extensionVersion.win-x64.zip"
    }

    #Build site extension
    Write-Host "privateSiteExtensionPath: " $privateSiteExtensionPath
    Rename-Item "$privateSiteExtensionPath" "$siteExtensionPath\$extensionVersion"
    Copy-Item .\src\WebJobs.Script.WebHost\extension.xml "$siteExtensionPath"
    ZipContent $siteExtensionPath "$buildOutput\Functions.$extensionVersion$runtimeSuffix.zip"

}

function cleanExtension([string] $bitness) {
    Remove-Item -Recurse -Force "$privateSiteExtensionPath\$bitness\publish" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "$privateSiteExtensionPath\$bitness\runtimes\linux" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "$privateSiteExtensionPath\$bitness\runtimes\osx" -ErrorAction SilentlyContinue

    Get-ChildItem "$privateSiteExtensionPath\$bitness\workers\node\grpc\src\node\extension_binary" -ErrorAction SilentlyContinue | 
    Foreach-Object {
        if (-Not ($_.FullName -Match "win32")) {
            Remove-Item -Recurse -Force $_.FullName
        }
    }

    $keepRuntimes = @('win', 'win-x86', 'win10-x86')
    Get-ChildItem "$privateSiteExtensionPath\$bitness\workers\powershell\runtimes" -Exclude $keepRuntimes -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
  
dotnet --version
dotnet build .\WebJobs.Script.sln -v q /p:BuildNumber="$buildNumber"

$projects = 
  "WebJobs.Script",
  "WebJobs.Script.WebHost",
  "WebJobs.Script.Grpc"
  
foreach ($project in $projects)
{

  $cmd = "pack", "src\$project\$project.csproj", "-o", "..\..\buildoutput", "--no-build" , "-p:PackageVersion=$extensionVersion"
  
  & dotnet $cmd  
}

$cmd = "pack", "tools\WebJobs.Script.Performance\WebJobs.Script.Performance.App\WebJobs.Script.Performance.App.csproj", "-o", "..\..\..\buildoutput"
& dotnet $cmd

$cmd = "pack", "tools\ExtensionsMetadataGenerator\src\ExtensionsMetadataGenerator\ExtensionsMetadataGenerator.csproj", "-o", "..\..\..\..\buildoutput", "-c", "Release"
& dotnet $cmd

$bypassPackaging = $env:APPVEYOR_PULL_REQUEST_NUMBER -and -not $env:APPVEYOR_PULL_REQUEST_TITLE.Contains("[pack]")

if ($bypassPackaging){
    Write-Host "Bypassing artifact packaging and CrossGen for pull request." -ForegroundColor Yellow
} else {
    AddDiaSymReaderToPath

    # build no-runntime extension
    BuildPackages 1

    #build win-x86 and win-x64 extension
    BuildPackages 0

    & ".\tools\RunSigningJob.ps1" 
    if (-not $?) { exit 1 }
}