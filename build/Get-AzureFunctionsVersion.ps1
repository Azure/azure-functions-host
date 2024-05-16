# use the same logic as the projects to generate the site extension version
$cmd = "build", "$PSScriptRoot/../src/WebJobs.Script/WebJobs.Script.csproj", "-t:EchoVersion", "--no-restore", "--nologo", "-clp:NoSummary"
$version = (& dotnet $cmd).Trim()
return $version
