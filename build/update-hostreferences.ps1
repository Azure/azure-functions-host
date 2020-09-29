    $response = $null
    try
    {
        $url = "https://raw.githubusercontent.com/Azure/azure-functions-integration-tests/main/integrationTestsBuild/V3/HostBuild.json"
          $response = Invoke-WebRequest -Uri $url -ErrorAction Stop       
           }
    catch
    {
        throw "Failed to download package list from '$url'"
    }
    if (-not $response.Content)
    {
        throw "Failed to download package list. Verify that the file located at '$url' is not empty."
    }
    $packagesToUpdate = @($response.Content | ConvertFrom-Json)

    # Update packages references
    write-host "Updating Package references"
    $source = "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsPreRelease/nuget/v3/index.json"
    
    $currentDirectory = Get-Location

    $path = "$PSScriptRoot\..\src\WebJobs.Script"
    if (-not (Test-Path $path))
    {
        throw "Failed to find '$path' to update package references"
    }

    try
    {
        set-location $path

        foreach ($package in $packagesToUpdate)
        {
            $packageInfo = & {NuGet list $package -Source $source}
                 $packageName = $packageInfo.Split()[0]
            $packageVersion = $packageInfo.Split()[1]

            Write-host "Adding $packageName $packageVersion to project" -ForegroundColor Green
            & { dotnet add package $packageName -v $packageVersion -s $source }
        }
    }
    finally
    {
        Set-Location $currentDirectory
    }
