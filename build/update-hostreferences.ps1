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
WriteLog "Script started."
# Make sure the project path exits
$path = "$PSScriptRoot\..\src\WebJobs.Script"
if (-not (Test-Path $path))
{
    WriteLog "Failed to find '$path' to update package references" -Throw
}
# Download the list of pacakges to update
$response = $null
try
{
    $url = "https://raw.githubusercontent.com/Azure/azure-functions-integration-tests/main/integrationTestsBuild/V3/HostBuild.json"
    $response = Invoke-WebRequest -Uri $url -ErrorAction Stop       
}
catch
{
    WriteLog "Failed to download package list from '$url'" -Throw
}
if (-not $response.Content)
{
    WriteLog "Failed to download package list. Verify that the file located at '$url' is not empty." -Throw
}
$packagesToUpdate = @($response.Content | ConvertFrom-Json)
if (!$packagesToUpdate.Count)
{
    WriteLog "There are no packages to update in '$url'" -Throw
}

# Update packages references
WriteLog "Updating Package references"
$source = "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsPreRelease/nuget/v3/index.json"
$currentDirectory = Get-Location
try
{
    set-location $path
    foreach ($package in $packagesToUpdate)
    {
        $packageInfo = & {NuGet list $package -Source $source}
        $packageName = $packageInfo.Split()[0]
        $packageVersion = $packageInfo.Split()[1]
        WriteLog "Adding '$packageName' '$packageVersion' to project"
        & { dotnet add package $packageName -v $packageVersion -s $source }
        if ($LASTEXITCODE -ne 0)
        {
            WriteLog "dotnet add package $packageName -v $packageVersion -s $source failed" -Throw    
        }        
    }
}
finally
{
    Set-Location $currentDirectory
}
WriteLog "Script completed."