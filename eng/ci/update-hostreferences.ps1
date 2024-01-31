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

Class PackageInfo {
    [string]$Name
    [string]$Version
}

function NewPackageInfo
{
    param (
        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]
        $PackageInformation
    )

    $parts = $PackageInformation.Split(" ")

    if ($parts.Count -gt 2)
    {
        WriteLog "Invalid package format. The string should only contain 'name<space>version'. Current value: '$PackageInformation'"
    }

    $packageInfo = [PackageInfo]::New()
    $packageInfo.Name = $parts[0]
    $packageInfo.Version = $parts[1]

    return $packageInfo
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
    $includeAllVersion = if (-not [string]::IsNullOrWhiteSpace($MajorVersion)) { "-AllVersions" } else { "" }

    $packageInfo = & { NuGet list $Name -Source $SOURCE -PreRelease $includeAllVersion }

    if ($packageInfo -like "*No packages found*")
    {
        WriteLog "Package name $Name not found in $SOURCE." -Throw
    }

    if (-not $MajorVersion)
    {
        $result = NewPackageInfo -PackageInformation $packageInfo
    }
    else
    {
        foreach ($thisPackage in $packageInfo.Split([System.Environment]::NewLine))
        {
            $package = NewPackageInfo -PackageInformation $thisPackage

            if ($package.Version.StartsWith($MajorVersion))
            {
                $result = $package
                break
            }
        }
    }

    return $result
}

WriteLog "Script started."

# Make sure the project path exits
$path = "$PSScriptRoot\..\..\src\WebJobs.Script"
if (-not (Test-Path $path))
{
    WriteLog "Failed to find '$path' to update package references" -Throw
}

$URL = "https://raw.githubusercontent.com/Azure/azure-functions-integration-tests/main/integrationTestsBuild/V4/HostBuild.json"
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
        $packageInfo = GetPackageInfo -Name $package.Name -MajorVersion $package.MajorVersion

        if ($package.Name -eq "Microsoft.Azure.Functions.PythonWorker")
        {
            # The PythonWorker is not defined in the src/WebJobs.Script/WebJobs.Script.csproj. It is defined in eng/targets/python.props.
            # To update the package version, the xml file eng/targets/python.props needs to be updated directly.
            $pythonPropsFilePath = "$PSScriptRoot\..\targets\python.props"

            if (-not (Test-Path $pythonPropsFilePath))
            {
                WriteLog "Python Props file '$pythonPropsFilePath' does not exist." -Throw
            }

            WriteLog "Set Python package version in '$pythonPropsFilePath' to '$($packageInfo.Version)'"

            # Read the xml file
            [xml]$xml = Get-Content $pythonPropsFilePath -Raw -ErrorAction Stop

            # Replace the package version
            $xml.Project.ItemGroup.PackageReference.Version = $packageInfo.Version

            # Save the file
            $xml.Save($pythonPropsFilePath)

            if ($LASTEXITCODE -ne 0)
            {
                WriteLog "Failed to update Python Props file" -Throw
            }
        }
        else
        {
            WriteLog "Adding '$($packageInfo.Name)' '$($packageInfo.Version)' to project"
            & { dotnet add package $packageInfo.Name -v $packageInfo.Version -s $SOURCE --no-restore }

            if ($LASTEXITCODE -ne 0)
            {
                WriteLog "dotnet add package '$($packageInfo.Name)' -v '$($packageInfo.Version)' -s $SOURCE --no-restore failed" -Throw
            }
        }
    }
}
finally
{
    Set-Location $currentDirectory
}

WriteLog "Script completed."