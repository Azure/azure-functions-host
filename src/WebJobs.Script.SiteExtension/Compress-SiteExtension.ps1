<#
    .SYNOPSIS
    Compresses the site extension.

    .DESCRIPTION
    Takes in an unzipped site extension and produces a site extension.

    .PARAMETER InputPath
    The path of the unzipped 'SiteExtension'. Leave null to scan for root in a child from here.

    .PARAMETER OutputPath
    The path to produce the site extension to. Leave null to use current directory.

    .PARAMETER JitFile
    [Array] The path of the JIT trace profiles to include in the site extension.

    .PARAMETER Force
    [Switch] Include to overwrite existing files.

    .INPUTS
    None. You can't pipe objects to Compress-SiteExtension.ps1.

    .OUTPUTS
    None. Compress-SiteExtension.ps1 doesn't generate any output.
#>

param (
    [string] $InputPath = $null,
    [string] $OutputPath = $null,
    [string[]] $JitFile = @(),
    [switch] $Force
)

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
    $OutputPath = (Split-Path $InputPath -Leaf) + ".zip"
}

if (Test-Path $OutputPath)
{
    if ($Force)
    {
        Remove-Item -Path $OutputPath -Recurse -Force
    }
    else
    {
        Write-Error "OutputPath already exists. Use -Force to overwrite."
        exit 1
    }
}

if ($JitFile)
{
    $destinations = Get-ChildItem -Path $InputPath -Filter .jitmarker -Recurse
    $JitFile | ForEach-Object {
        $file = $_
        $destinations | ForEach-Object {
            Write-Host "Copying JIT trace file $file to $($_.Directory)"
            Copy-Item -Path $file -Destination $_.Directory -Force
        }
    }
}

try
{
    Compress-Archive -Path "$InputPath/*" -DestinationPath $OutputPath
    Write-Host "Published site extension to $OutputPath"
}
finally
{
    # Cleanup JitTrace files
    if ($JitFile)
    {
        $JitFile | ForEach-Object {
            $file = Split-Path $_ -Leaf
            $destinations | ForEach-Object {
                Remove-Item -Path (Join-Path $_.Directory $file)
            }
        }
    }
}
