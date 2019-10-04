param (
  [string]$buildNumber = "0",
  [string]$extensionVersion = "3.0.$buildNumber",
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

function BuildPackages([bool] $isNoRuntime) {
    if($isNoRuntime) {
        BuildOutput ""
        $applicationHost = Get-Content $buildOutput\publish.no-runtime\SiteExtensions\Functions\applicationHost.xdt
        $applicationHost -replace "\\%XDT_BITNESS%","" | Out-File $buildOutput\publish.no-runtime\SiteExtensions\Functions\applicationHost.xdt

        CreateZips ".no-runtime"
    } else {        
        BuildOutput "win-x86" 1
        BuildOutput "win-x64" 1
        BuildOutput "win-x86" 0
        BuildOutput "win-x64" 0               

        New-Item -Itemtype directory -path $buildOutput\publish.runtime\SiteExtensions\Functions
        Move-Item -Path $buildOutput\publish.win-x86\SiteExtensions\Functions -Destination $buildOutput\publish.runtime\SiteExtensions\Functions\32bit -Force
        Move-Item -Path $buildOutput\publish.win-x64\SiteExtensions\Functions -Destination $buildOutput\publish.runtime\SiteExtensions\Functions\64bit -Force
        Copy-Item -Path $buildOutput\publish.runtime\SiteExtensions\Functions\32bit\applicationHost.xdt -Destination $buildOutput\publish.runtime\SiteExtensions\Functions -Force

        # To minimize size skip 64bit folder.
        # Until site extension is in Antares, use self-contained.
        New-Item -Itemtype directory -path $buildOutput\publish.self-contained\SiteExtensions\Functions
        Move-Item -Path $buildOutput\publish.win-x86.self-contained\SiteExtensions\Functions -Destination $buildOutput\publish.self-contained\SiteExtensions\Functions\32bit -Force
        Copy-Item -Path $buildOutput\publish.runtime\SiteExtensions\Functions\applicationHost.xdt -Destination $buildOutput\publish.self-contained\SiteExtensions\Functions -Force
        
        #Remove-Item -Path $buildOutput\publish.runtime\SiteExtensions\Functions\64bit\applicationHost.xdt

        CreateZips ".runtime"
    }
}


function BuildOutput([string] $runtime, [bool] $isSelfContained) {
    $runtimeSuffix = ""
    $ridSwitch = ""
    $hasRuntime = ![string]::IsNullOrEmpty($runtime)

    if ($hasRuntime -and !$isSelfContained) {
        Write-Host "Building $runtime"
        $runtimeSuffix = ".$runtime"
    		$ridSwitch = "-r", "$runtime", "--self-contained", "false", "/p:PublishReadyToRun=true"
    } elseif ($hasRuntime -and $isSelfContained) {
        Write-Host "Building $runtime self-contained"
        $runtimeSuffix = ".$runtime.self-contained"        
		    $ridSwitch = "-r", "$runtime", "--self-contained", "true", "/p:PublishReadyToRun=true", "/p:PublishReadyToRunEmitSymbols=true"
    } else {
        $runtimeSuffix = ".no-runtime"
    }

    $publishTarget = "$buildOutput\publish$runtimeSuffix"
    $siteExtensionPath = "$publishTarget\SiteExtensions"
    $privateSiteExtensionPath = "$siteExtensionPath\Functions"
    
    New-Item -Itemtype directory -path $privateSiteExtensionPath
    
	  dotnet publish .\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj $ridSwitch -o "$privateSiteExtensionPath" -v q /p:BuildNumber=$buildNumber /p:IsPackable=false -c Release
    
    if ($hasRuntime -and $isSelfContained) {
        Write-Host "Moving symbols"
        New-Item -Itemtype directory -path $publishTarget\Symbols
        Move-Item -Path $privateSiteExtensionPath\*.pdb -Destination $publishTarget\Symbols
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

        # Create private extension for internal usage.
        $tempPath = "$buildOutput\win-x32.inproc.temp\SiteExtensions"

        # Make a temp location
        New-Item -Itemtype directory -path $tempPath -ErrorAction SilentlyContinue
       
        # Copy all files to temp folder
        $selfContainedPath = "$buildOutput\publish.self-contained\SiteExtensions\Functions"
        Copy-Item -Path $selfContainedPath -Destination $tempPath -Recurse

        # Make the zip
        ZipContent "$buildOutput\win-x32.inproc.temp" "$buildOutput\Functions.Private.$extensionVersion.win-x32.inproc.zip"

        Remove-Item $tempPath -Recurse
    }

    # Zip up symbols for builds with runtime embedded
    if ($runtimeSuffix -eq  "") {
        ZipContent "$buildOutput\publish.win-x86.self-contained\Symbols" "$buildOutput\Functions.Symbols.$extensionVersion.win-x86.zip"
        ZipContent "$buildOutput\publish.win-x64.self-contained\Symbols" "$buildOutput\Functions.Symbols.$extensionVersion.win-x64.zip"
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
    Remove-Item -Recurse -Force "$privateSiteExtensionPath\$bitness\workers\python" -ErrorAction SilentlyContinue

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

  $cmd = "pack", "src\$project\$project.csproj", "-o", "$buildOutput", "--no-build" , "-p:PackageVersion=$extensionVersion"

  & dotnet $cmd  
}

$cmd = "pack", "tools\WebJobs.Script.Performance\WebJobs.Script.Performance.App\WebJobs.Script.Performance.App.csproj", "-o", "$buildOutput"
& dotnet $cmd

$cmd = "pack", "tools\ExtensionsMetadataGenerator\src\ExtensionsMetadataGenerator\ExtensionsMetadataGenerator.csproj", "-o", "$buildOutput", "-c", "Release"
& dotnet $cmd

$bypassPackaging = $env:APPVEYOR_PULL_REQUEST_NUMBER -and -not $env:APPVEYOR_PULL_REQUEST_TITLE.Contains("[pack]")

if ($bypassPackaging){
    Write-Host "Bypassing artifact packaging and CrossGen for pull request." -ForegroundColor Yellow
} else {
    # build no-runtime extension
    BuildPackages 1

    #build win-x86 and win-x64 extension
    BuildPackages 0

    & ".\tools\RunSigningJob.ps1" 
    if (-not $?) { exit 1 }
}