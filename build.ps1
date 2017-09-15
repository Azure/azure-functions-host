param (
  [string]$buildNumber = "0",
  [string]$extensionVersion = "2.0.$buildNumber",
  [string]$versionSuffix = "$buildNumber"
)

$currentDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildOutput = Join-Path $currentDir "buildoutput"
$publishTarget = "$buildOutput\publish"
$siteExtensionPath = "$publishTarget\SiteExtensions"
$privateSiteExtensionPath = "$siteExtensionPath\Functions"

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

dotnet --version
dotnet build .\WebJobs.Script.sln -v q /p:BuildNumber="$buildNumber"
dotnet pack src\WebJobs.Script\WebJobs.Script.csproj -o ..\..\buildoutput --no-build --version-suffix $versionSuffix
dotnet pack src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj -o ..\..\buildoutput --no-build --version-suffix $versionSuffix
dotnet pack src\WebJobs.Script.Grpc\WebJobs.Script.Grpc.csproj -o ..\..\buildoutput --no-build --version-suffix $versionSuffix

dotnet publish .\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj -o "$privateSiteExtensionPath" -v q /p:BuildNumber=$buildNumber

# Project cleanup (trim some project files - this should be revisited)
Remove-Item -Recurse -Force "$privateSiteExtensionPath\publish" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$privateSiteExtensionPath\runtimes\linux" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$privateSiteExtensionPath\runtimes\osx" -ErrorAction SilentlyContinue

# Create site extension packages
ZipContent $publishTarget "$buildoutput\Functions.Private.$extensionVersion.zip"

#Build site extension
Rename-Item "$privateSiteExtensionPath" "$siteExtensionPath\$extensionVersion"
Copy-Item .\src\WebJobs.Script.WebHost\extension.xml "$siteExtensionPath"
ZipContent $siteExtensionPath "$buildoutput\Functions.$extensionVersion.zip"
