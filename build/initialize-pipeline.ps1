$buildReason = $env:BUILD_REASON
$sourceBranch = $env:BUILD_SOURCEBRANCH
$artifactsBuildNumber =  $env:buildNumber

Write-Host "BUILD_REASON: '$buildReason'"
Write-Host "BUILD_SOURCEBRANCH: '$sourceBranch'"
Write-Host "ArtifactsBuildNumber: '$artifactsBuildNumber'"

function SetBuildArtifactsToTrue
{
  Write-Host "##vso[task.setvariable variable=BuildArtifacts;isOutput=true]true"
  Write-Host "Setting 'BuildArtifacts' to true."
}

if ($buildReason -eq "PullRequest") {
  # parse PR title to see if we should pack this
  $response = Invoke-RestMethod api.github.com/repos/$env:BUILD_REPOSITORY_ID/pulls/$env:SYSTEM_PULLREQUEST_PULLREQUESTNUMBER
  $title = $response.title.ToLowerInvariant()
  Write-Host "Pull request '$title'"
  if ($title.Contains("[pack]")) {
    SetBuildArtifactsToTrue
  }
}

$setBuildNumberForIntegrationTesting = $null
if ($env:RUNBUILDFORINTEGRATIONTESTS)
{
  if (-not ([bool]::TryParse($env:RUNBUILDFORINTEGRATIONTESTS, [ref] $setBuildNumberForIntegrationTesting)))
  {
    throw "RUNBUILDFORINTEGRATIONTESTS can only be set to True or False. Current value is set to $env:RUNBUILDFORINTEGRATIONTESTS"
  }
  Write-Host "buildHostForIntegrationTesting: $setBuildNumberForIntegrationTesting"
}

<#
# Note: This logic might not be needed anymore.

$buildNumber = ""

if (($buildReason -eq "PullRequest") -or !($sourceBranch.ToLower().Contains("release/4.")))
{
  $buildNumber = $env:buildNumber
  Write-Host "BuildNumber: '$buildNumber'"
}
#>

if ($setBuildNumberForIntegrationTesting)
{
  Write-Host "Build the Functions Host for integration testing."

  # The maximum value for the build number that can be used is 65535. For more information,
  # please see https://docs.microsoft.com/en-us/archive/blogs/msbuild/why-are-build-numbers-limited-to-65535

  # Generate a build number based on the month and day, e.g., 0217 for 02/17
  $now = Get-Date
  $timeZoneInfo  = [TimeZoneInfo]::FindSystemTimeZoneById("Pacific Standard Time")
  $buildTimeStamp = [TimeZoneInfo]::ConvertTime($now, $timeZoneInfo).ToString("MMdd")

  # Set the build number
  Write-Host "Setting ArtifactsBuildNumber"
  $artifactsBuildNumber = $buildTimeStamp
  Write-Host "ArtifactsBuildNumber: '$artifactsBuildNumber'"

  SetBuildArtifactsToTrue
}

Import-Module $PSScriptRoot\Get-AzureFunctionsVersion -Force
$version = Get-AzureFunctionsVersion $artifactsBuildNumber $artifactsBuildNumber

Write-Host "Site extension version: $version"
Write-Host "##vso[build.updatebuildnumber]$version"

Write-Host "This is the build number to be used for building the artifacts"
Write-Host "##vso[task.setvariable variable=ArtifactsBuildNumber;isOutput=true]$artifactsBuildNumber"
Write-Host "ArtifactsBuildNumber: $artifactsBuildNumber"
