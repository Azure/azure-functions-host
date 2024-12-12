<#
    .SYNOPSIS
    Produces a private site extension.

    .DESCRIPTION
    Takes in a published site extension and produces a private site extension.

    .PARAMETER InputPath
    The path of the published 'SiteExtension'. Default is "./SiteExtension".

    .PARAMETER OutputPath
    The path to produce the private site extension to. When zipping, this is the name of the zip file. Default is "./PrivateSiteExtension".

    .PARAMETER Zip
    [Switch] Include to produce site extension as a zip.

    .PARAMETER Force
    [Switch] Include to overwrite existing files.

    .INPUTS
    None. You can't pipe objects to Update-Month.ps1.

    .OUTPUTS
    None. Update-Month.ps1 doesn't generate any output.
#>

param (
    [string] $InputPath = "./SiteExtension",
    [string] $OutputPath = "./PrivateSiteExtension",
    [switch] $Zip,
    [switch] $Force
)

if (-not (Join-Path $InputPath "extension.xml" | Test-Path))
{
    Write-Error "InputPath should be the path to the root of 'SiteExtension' folder (where 'extension.xml' is)."
    Write-Error "Make sure to publish the site extension before running this script."
    exit 1
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

    $inputPath = Get-ChildItem -Path .\SiteExtension\ -Attributes Directory
    $outputPath = Join-Path $outputPath "SiteExtensions" "Functions"
    New-Item -ItemType Directory -Path $outputPath | Out-Null

    Copy-Item "$inputPath/applicationHost.xdt" -Destination $outputPath | Out-Null

    $filesDest = (Join-Path $outputPath "32bit")
    Copy-Item "$inputPath/32bit/" -Destination "$filesDest/" -Container -Recurse | Out-Null

    $workerDest = (Join-Path $filesDest "workers")
    Copy-Item "$inputPath/workers/" -Destination "$workerDest/" -Container -Recurse | Out-Null
}

if ($Zip) {
    if (-not $OutputPath.EndsWith(".zip")) {
        $OutputPath = "$OutputPath.zip"
    }

    Write-Zip $OutputPath
} else {
    Write-Folder $OutputPath
}

Write-Host "Published private site extension to $OutputPath"
