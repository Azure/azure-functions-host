$buildReason = $env:BUILD_REASON
$sourceBranch = $env:BUILD_SOURCEBRANCH

Write-Host "BUILD_REASON: '$buildReason'"
Write-Host "BUILD_SOURCEBRANCH: '$sourceBranch'"

if ($buildReason -eq "PullRequest") {
  # parse PR title to see if we should pack this
  $response = Invoke-RestMethod api.github.com/repos/$env:BUILD_REPOSITORY_ID/pulls/$env:SYSTEM_PULLREQUEST_PULLREQUESTNUMBER
  $title = $response.title.ToLowerInvariant()
  Write-Host "Pull request '$title'"
  if ($title.Contains("[pack]")) {
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
$version = Get-AzureFunctionsVersion

Write-Host "Site extension version: $version"
Write-Host "##vso[build.updatebuildnumber]$version"