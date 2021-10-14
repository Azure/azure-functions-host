function WriteLog
{
    param (
        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]
        $Message,

        [Switch]
        $Throw
    )

    $Message = (Get-Date -Format G)  + " -- $Message"

    if ($Throw)
    {
        throw $Message
    }

    Write-Host $Message
}

function ValidateNugetListOutput
{
    param (
        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]
        $Output,

        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]
        $Command
    )

    if ($Output -like "*No packages found*")
    {
        WriteLog "Failed to get the latest package information via: $Command" -Throw
    }
}

function GetPackageInfo
{
    param (
        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]
        $Name,

        [Parameter(Mandatory=$false)]
        [System.String]
        $MajorVersion
    )

    $result = $null

    if (-not [string]::IsNullOrWhiteSpace($MajorVersion))
    {
        $packageInfo = & { NuGet list $Name -Source $SOURCE -PreRelease -AllVersions }

        $command = "NuGet list $Name -Source $SOURCE -PreRelease -AllVersions"
        ValidateNugetListOutput -Output ($packageInfo | Out-String) -Command $command

        foreach ($package in $packageInfo)
        {
            $packageVersion = $package.Split()[1]
            if ($packageVersion.StartsWith($MajorVersion))
            {
                $result = $package
                break
            }
        }
    }
    else
    {
        $packageInfo = & { NuGet list $Name -Source $SOURCE -PreRelease }

        $command = "NuGet list $Name -Source $SOURCE -PreRelease"
        ValidateNugetListOutput -Output ($packageInfo | Out-String) -Command $command

        $result = $packageInfo
    }

    if (-not $result)
    {
        WriteLog "Failed to get the latest package information for '$Name'" -Throw
    }

    return $result
}

WriteLog "Script started."

# Make sure the project path exits
$path = "$PSScriptRoot\..\src\WebJobs.Script"
if (-not (Test-Path $path))
{
    WriteLog "Failed to find '$path' to update package references" -Throw
}

$URL = "https://raw.githubusercontent.com/Azure/azure-functions-integration-tests/dev/integrationTestsBuild/V3/HostBuild2.json"
$SOURCE = "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsPreRelease/nuget/v3/index.json"

WriteLog "Get the list of packages to update"

$packagesToUpdate = Invoke-RestMethod -Uri $URL -ErrorAction Stop
if ($packagesToUpdate.Count -eq 0)
{
    WriteLog "There are no packages to update in '$URL'" -Throw
}

# Update packages references
WriteLog "Package references to update: $($packagesToUpdate.Count)"

$currentDirectory = Get-Location
try
{
    set-location $path

    foreach ($package in $packagesToUpdate)
    {
        WriteLog "Package name: $($package.Name)"

        $packageInfo = GetPackageInfo -Name $package.Name -MajorVersion $package.MajorVersion

        WriteLog "AzureFunctionsPreRelease latest package info --> $packageInfo"
        $packageName = $packageInfo.Split()[0]
        $packageVersion = $packageInfo.Split()[1]

        if ($package -eq "Microsoft.Azure.Functions.PythonWorker")
        {
            # The PythonWorker is not defined in the src/WebJobs.Script/WebJobs.Script.csproj. It is defined in build/python.props.
            # To update the package version, the xml file build/python.props needs to be updated directly.
            $pythonPropsFilePath = "$PSScriptRoot\python.props"

            if (-not (Test-Path $pythonPropsFilePath))
            {
                WriteLog "Python Props file '$pythonPropsFilePath' does not exist." -Throw
            }

            WriteLog "Set Python package version in '$pythonPropsFilePath' to '$packageVersion'"

            # Read the xml file
            [xml]$xml = Get-Content $pythonPropsFilePath -Raw -ErrorAction Stop

            # Replace the package version
            $xml.Project.ItemGroup.PackageReference.Version = $packageVersion

            # Save the file
            $xml.Save($pythonPropsFilePath)

            if ($LASTEXITCODE -ne 0)
            {
                WriteLog "Failed to update Python Props file" -Throw
            }
        }
        else
        {
            WriteLog "Adding '$packageName' '$packageVersion' to project"
            & { dotnet add package $packageName -v $packageVersion -s $source --no-restore }

            if ($LASTEXITCODE -ne 0)
            {
                WriteLog "dotnet add package $packageName -v $packageVersion -s $source --no-restore failed" -Throw
            }
        }
    }
}
finally
{
    Set-Location $currentDirectory
}

WriteLog "Script completed."