$projectPath = "$PSScriptRoot\..\..\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj"
if (-not (Test-Path $projectPath))
{
    throw "Project path '$projectPath' does not exist."
}

$cmd = "list", $projectPath, "package", "--include-transitive", "--vulnerable"
Write-Host "dotnet $cmd"
dotnet $cmd | Tee-Object -Variable output

$result = $output | Select-String "has no vulnerable packages given the current sources"

if (!$result)
{
  Write-Host "Vulnerabilities found" 
  Exit 1
}
