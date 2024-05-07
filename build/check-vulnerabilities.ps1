$projectPath = "$PSScriptRoot\..\src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj"
$logFilePath = "$PSScriptRoot\..\build.log"
if (-not (Test-Path $projectPath))
{
    throw "Project path '$projectPath' does not exist."
}

$cmd = "list", $projectPath, "package", "--include-transitive", "--vulnerable"
Write-Host "dotnet $cmd"
dotnet $cmd | Tee-Object build.log

$result = Get-content $logFilePath | select-string "has no vulnerable packages given the current sources"

$logFileExists = Test-Path $logFilePath -PathType Leaf
if ($logFileExists)
{
  Remove-Item $logFilePath
}

if (!$result)
{
  Write-Host "Vulnerabilities found" 
  Exit 1
}
