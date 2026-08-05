$ErrorActionPreference = "Stop"

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$testProject = Join-Path $repositoryRoot "test\WebJobs.Script.Tests\WebJobs.Script.Tests.csproj"
$previousUpdateValue = $env:UPDATE_ENVIRONMENT_MIGRATION_BASELINES
$locationPushed = $false

try {
  $env:UPDATE_ENVIRONMENT_MIGRATION_BASELINES = "1"
  Push-Location $repositoryRoot
  $locationPushed = $true

  & dotnet test $testProject --filter "FullyQualifiedName=Microsoft.Azure.WebJobs.Script.Tests.StaticAnalysis.EnvironmentMigrationSourceUsageTests.BaselinesMatchCurrentSource"
  if ($LASTEXITCODE -ne 0) {
    throw "Environment migration baseline refresh failed with exit code $LASTEXITCODE."
  }
}
finally {
  if ($locationPushed) {
    Pop-Location
  }
  $env:UPDATE_ENVIRONMENT_MIGRATION_BASELINES = $previousUpdateValue
}
