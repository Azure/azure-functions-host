param (
  [string]$buildNumber = "0",
  [string]$extensionVersion = "2.0.$buildNumber",
  [bool]$includeVersion = $true
)

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

function CrossGen([string] $runtime, [bool] $isSelfContained, [string] $publishTarget, [string] $privateSiteExtensionPath)
{
    Write-Host "publishTarget: " $publishTarget
    Write-Host "privateSiteExtensionPath: " $privateSiteExtensionPath

    $selfContained = Join-Path $publishTarget "self-contained"
    $regular = Join-Path $publishTarget "regular"
    $crossGen = "$publishTarget\download\crossgen\crossgen.exe"

    #DownloadNupkg "https://dotnet.myget.org/F/dotnet-core/api/v2/package/runtime.$runtime.microsoft.netcore.jit/2.1.0-preview2-26316-09" @("runtimes\$runtime\native\clrjit.dll")  @("$publishTarget\download\clrjit")
    #DownloadNupkg "https://dotnet.myget.org/F/dotnet-core/api/v2/package/runtime.$runtime.Microsoft.NETCore.Runtime.CoreCLR/2.1.0-preview2-26316-09"  @("tools\crossgen.exe")  @("$publishTarget\download\crossgen")

    DownloadNupkg "https://www.nuget.org/api/v2/package/runtime.$runtime.Microsoft.NETCore.Jit/2.0.5" @("runtimes\$runtime\native\clrjit.dll")  @("$publishTarget\download\clrjit")
    DownloadNupkg "https://www.nuget.org/api/v2/package/runtime.$runtime.Microsoft.NETCore.Runtime.CoreCLR/2.0.5"  @("tools\crossgen.exe")  @("$publishTarget\download\crossgen")
    DownloadNupkg "https://www.nuget.org/api/v2/package/Microsoft.Build.Tasks.Core/15.1.1012" @("lib\netstandard1.3\Microsoft.Build.Tasks.Core.dll")  @("$selfContained")
    DownloadNupkg "https://www.nuget.org/api/v2/package/Microsoft.Build.Utilities.Core/15.1.1012" @("lib\netstandard1.3\Microsoft.Build.Utilities.Core.dll")  @("$selfContained")

    # Publish self-contained app with all required dlls for crossgen
    dotnet publish .\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj -r $runtime -o "$selfContained" -v q /p:BuildNumber=$buildNumber    

    # For self contained we want to process only IL ddls
    if ($isSelfContained) {
        dotnet publish .\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj -o $regular -v q /p:BuildNumber=$buildNumber
    }

    $successfullDlls =@()
    $failedDlls = @()
    Get-ChildItem $privateSiteExtensionPath -Filter *.dll | 
    Foreach-Object {

        # For self contained we want to process only IL ddls
        if ($isSelfContained) {
            if (-Not [System.IO.File]::Exists($(Join-Path $regular $_.Name))) {
                Write-Host "Skipping $_.Name"
                Return
            }
        }

        $prm = "/JITPath", "$publishTarget\download\clrjit\clrjit.dll", "/Platform_Assemblies_Paths", "$selfContained", "/nologo", "/in", $_.FullName
        # output for Microsoft.Azure.WebJobs.Script.WebHost.dll is Microsoft.Azure.WebJobs.Script.WebHost.exe.dll by default
        if ($_.FullName -like "*Microsoft.Azure.WebJobs.Script.WebHost.dll") {
            $prm += "/out"        
            $prm += Join-Path $privateSiteExtensionPath "Microsoft.Azure.WebJobs.Script.WebHost.ni.dll"
        }

        & $crossGen $prm >> $buildOutput\crossgenout.$runtime.txt

        $niDll = Join-Path $privateSiteExtensionPath $([io.path]::GetFileNameWithoutExtension($_.FullName) + ".ni.dll")
        if ([System.IO.File]::Exists($niDll)) {
            Remove-Item $_.FullName
            Rename-Item -Path $niDll -NewName $_.FullName
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
    
    
    if (-Not $isSelfContained) {
        Copy-Item -Path $privateSiteExtensionPath\runtimes\win\native\*  -Destination $privateSiteExtensionPath -Force
    }         

    #read-host "Press ENTER to continue..."
    Remove-Item -Recurse -Force $selfContained -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $publishTarget\download -ErrorAction SilentlyContinue
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


function BuildPackages([string] $runtime, [bool] $isSelfContained) {
    $runtimeSuffix = ""
    if (![string]::IsNullOrEmpty($runtime)) {
        $runtimeSuffix = ".$runtime"
    }   

    $publishTarget = "$buildOutput\publish$runtimeSuffix"
    $siteExtensionPath = "$publishTarget\SiteExtensions"
    $privateSiteExtensionPath = "$siteExtensionPath\Functions"
    
    if ($isSelfContained) {    
        dotnet publish .\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj  -r $runtime -o "$privateSiteExtensionPath" -v q /p:BuildNumber=$buildNumber /p:IsPackable=false
    } else {
        dotnet publish .\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj -o "$privateSiteExtensionPath" -v q /p:BuildNumber=$buildNumber /p:IsPackable=false
    }        

    # replace IL dlls with crossgen dlls
    if (![string]::IsNullOrEmpty($runtime)) {
        CrossGen $runtime $isSelfContained $publishTarget $privateSiteExtensionPath
    }
 
    ZipContent $privateSiteExtensionPath "$buildOutput\Functions.Binaries.$extensionVersion-alpha$runtimeSuffix.zip"

    # Project cleanup (trim some project files - this should be revisited)
    Remove-Item -Recurse -Force "$privateSiteExtensionPath\publish" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "$privateSiteExtensionPath\runtimes\linux" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "$privateSiteExtensionPath\runtimes\osx" -ErrorAction SilentlyContinue

    # Create site extension packages
    ZipContent $publishTarget "$buildOutput\Functions.Private.$extensionVersion-alpha$runtimeSuffix.zip"

    #Build site extension
    Write-Host "privateSiteExtensionPath: " $privateSiteExtensionPath
    Rename-Item "$privateSiteExtensionPath" "$siteExtensionPath\$extensionVersion-alpha"
    Copy-Item .\src\WebJobs.Script.WebHost\extension.xml "$siteExtensionPath"
    ZipContent $siteExtensionPath "$buildOutput\Functions.$extensionVersion-alpha$runtimeSuffix.zip"

}

dotnet --version
dotnet build .\WebJobs.Script.sln -v q /p:BuildNumber="$buildNumber"

$projects = 
  "WebJobs.Script",
  "WebJobs.Script.WebHost",
  "WebJobs.Script.Grpc"
  
foreach ($project in $projects)
{
  $cmd = "pack", "src\$project\$project.csproj", "-o", "..\..\buildoutput", "--no-build"
  
  if ($includeVersion)
  {
    $cmd += "--version-suffix", "-$buildNumber"
  }
  
  & dotnet $cmd  
}


# build IL extensions
BuildPackages "" $false

#build win-x86 extensions
BuildPackages "win-x86" $false