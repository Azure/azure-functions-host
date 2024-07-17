param (
  [ValidateSet("6", "8", "")][string]$minorVersionPrefix = ""
)

$buildReason = $env:BUILD_REASON
$sourceBranch = $env:BUILD_SOURCEBRANCH
$provider = $env:BUILD_REPOSITORY_PROVIDER

Write-Host "BUILD_REASON: '$buildReason'"
Write-Host "BUILD_SOURCEBRANCH: '$sourceBranch'"

# See if artifacts should be built
if ($buildReason -eq "PullRequest") {

  # first we see if [pack] is in the PR title.
  # This often gets rate limited, so it isn't reliable.

  $pack = $false

  if ($provider -eq "GitHub") {
    try {
      $response = Invoke-RestMethod api.github.com/repos/$env:BUILD_REPOSITORY_ID/pulls/$env:SYSTEM_PULLREQUEST_PULLREQUESTNUMBER
      $title = $response.title.ToLowerInvariant()
      Write-Host "Pull request '$title'"
      $pack = $title.Contains("[pack]")
    }
    catch {
      Write-Warning "Failed to get pull request title."
      Write-Warning $_
    }
  }

  # Next we check if the commit message contains '--pack'
  $commitMessage = $env:BUILD_SOURCEVERSIONMESSAGE
  if (!$pack && $commitMessage.Contains("--pack")) {
    $pack = $true
  }

  Write-Host "Pack: '$pack'"
  if ($pack) {
    Write-Host "##vso[task.setvariable variable=BuildArtifacts;isOutput=true]true"
    Write-Host "Setting 'BuildArtifacts' to true."
  }
}

$buildNumber = ""

$branch = $sourceBranch.ToLower();
$isRelease = $branch.Contains("release/4") -or $branch.Contains("release/inproc6/4") -or $branch.Contains("release/inproc8/4")

if(($buildReason -eq "PullRequest") -or !$isRelease)
{
  $buildNumber = $env:buildNumber
  Write-Host "BuildNumber: '$buildNumber'"
}
else 
{
  Write-Host "Release build; Not using a build number."
}

Import-Module $PSScriptRoot\Get-AzureFunctionsVersion -Force
$version = Get-AzureFunctionsVersion $buildNumber $buildNumber $minorVersionPrefix

Write-Host "Site extension version: $version"
Write-Host "##vso[build.updatebuildnumber]$version"
