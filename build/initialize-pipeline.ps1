$buildReason = $env:BUILD_REASON
$sourceBranch = $env:BUILD_SOURCEBRANCH

Write-Host "BUILD_REASON: '$buildReason'"
Write-Host "BUILD_SOURCEBRANCH: '$sourceBranch'"

# See if artifacts should be built
if ($buildReason -eq "PullRequest") {

  # first we see if [pack] is in the PR title.
  # This often gets rate limited, so it isn't reliable.

  $pack = $false
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

if(($buildReason -eq "PullRequest") -or !($sourceBranch.ToLower().Contains("release/4.")))
{
  $buildNumber = $env:buildNumber
  Write-Host "BuildNumber: '$buildNumber'"
}

Import-Module $PSScriptRoot\Get-AzureFunctionsVersion -Force
$version = Get-AzureFunctionsVersion $buildNumber $buildNumber

Write-Host "Site extension version: $version"
Write-Host "##vso[build.updatebuildnumber]$version"
