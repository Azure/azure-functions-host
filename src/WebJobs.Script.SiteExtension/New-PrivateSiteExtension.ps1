<#
    .SYNOPSIS
    Produces a private site extension.

    .DESCRIPTION
    Takes in a published site extension and produces a private site extension.

    .PARAMETER InputPath
    The path of the published 'SiteExtension'. Leave null to scan for root in a child from here.

    .PARAMETER OutputPath
    The path to produce the private site extension to (either the zip file or folder). Leave null to compute this name.

    .PARAMETER Bitness
    The bitness to produce the private site extension with. Default is '64bit'.

    .PARAMETER NoZip
    [Switch] Include to produce site extension as a folder and not a zip.

    .PARAMETER Force
    [Switch] Include to overwrite existing files.

    .INPUTS
    None. You can't pipe objects to Update-Month.ps1.

    .OUTPUTS
    None. Update-Month.ps1 doesn't generate any output.
#>

param (
    [string] $InputPath = $null,
    [string] $OutputPath = $null,
    [ValidateSet('x64', '64bit', 'x86', '32bit')][string] $Bitness = '64bit',
    [switch] $NoZip,
    [switch] $Force
)

$normalizeBitness = @{
    'x64' = '64bit'
    '64bit' = '64bit'
    'x86' = '64bit'
    '32bit' = '32bit'
}

$Bitness = $normalizeBitness[$Bitness]

if (-not $InputPath)
{
    $InputPath = (Get-ChildItem -Path . -Filter "extension.xml" -Recurse).Directory.FullName
}

if (Test-Path (Join-Path $InputPath "WebJobs.Script.SiteExtension.csproj"))
{
    Write-Error "This script should not be ran in the WebJobs.Script.SiteExtension project folder. Run this script in the root of the published site extension folder."
    exit 1
}

if (-not (Join-Path $InputPath "extension.xml" | Test-Path))
{
    Write-Error "Unable to find published site extension."
    exit 1
}

if (-not $OutputPath)
{
    $runtime = $Bitness -eq '32bit' ? 'win-x86' : 'win-x64'
    $leaf = (Split-Path $InputPath -Leaf)
    $split = $leaf.IndexOf('.')
    $OutputPath = "$($leaf.Substring(0, $split)).Private.$($leaf.Substring($split + 1)).$runtime"
}

function New-TemporaryDirectory {
    $tmp = [System.IO.Path]::GetTempPath()
    $name = (New-Guid).ToString("N")
    return New-Item -ItemType Directory -Path (Join-Path $tmp $name)
}

function Write-Zip ($outputPath)
{
    if (Test-Path $outputPath) {
        if ($Force) {
            Remove-Item -Path $outputPath -Recurse -Force
        } else {
            Write-Error "OutputPath already exists. Use -Force to overwrite."
            exit 1
        }
    }

    $tempDir = New-TemporaryDirectory
    Write-Folder $tempDir

    Compress-Archive -Path "$tempDir/*" -DestinationPath $outputPath
    Remove-Item -Path $tempDir -Recurse -Force
}

function Write-Folder ($outputPath)
{
    if (Test-Path "$outputPath/*") {
        if ($Force) {
            Remove-Item -Path $outputPath -Recurse -Force
        } else {
            Write-Error "OutputPath already exists. Use -Force to overwrite."
            exit 1
        }
    }

    $inputPath = Get-ChildItem -Path $InputPath -Attributes Directory
    $outputPath = Join-Path $outputPath "SiteExtensions" "Functions"
    New-Item -ItemType Directory -Path $outputPath | Out-Null

    Copy-Item "$inputPath/applicationHost.xdt" -Destination $outputPath | Out-Null

    $filesDest = (Join-Path $outputPath "$Bitness")
    Copy-Item "$inputPath/$Bitness/" -Destination "$filesDest/" -Container -Recurse | Out-Null

    $workerDest = (Join-Path $filesDest "workers")
    Copy-Item "$inputPath/workers/" -Destination "$workerDest/" -Container -Recurse | Out-Null
}

if ($NoZip) {
    Write-Folder $OutputPath
} else {
    if (-not $OutputPath.EndsWith(".zip")) {
        $OutputPath = "$OutputPath.zip"
    }

    Write-Zip $OutputPath
}

Write-Host "Published private site extension to $OutputPath"
