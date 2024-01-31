$buildReason = $env:BUILD_REASON
$sourceBranch = $env:BUILD_SOURCEBRANCH

Write-Host "BUILD_REASON: '$buildReason'"
Write-Host "BUILD_SOURCEBRANCH: '$sourceBranch'"

if ($buildReason -eq "PullRequest") {
  try {
    # parse PR title to see if we should pack this
    $response = Invoke-RestMethod api.github.com/repos/$env:BUILD_REPOSITORY_ID/pulls/$env:SYSTEM_PULLREQUEST_PULLREQUESTNUMBER
    $title = $response.title.ToLowerInvariant()
    Write-Host "Pull request '$title'"
    if ($title.Contains("[pack]")) {
      Write-Host "##vso[task.setvariable variable=BuildArtifacts;isOutput=true]true"
      Write-Host "Setting 'BuildArtifacts' to true."
    }
  }
  catch {
    Write-Warning "Failed to get pull request title. Artifacts will not be built or packed."
    Write-Warning $_
  }
}



Import-Module $PSScriptRoot\Get-AzureFunctionsVersion -Force
$version = Get-AzureFunctionsVersion
$builderNumber = $version

if ($env:BUILD_ISRELEASE) {
  Write-Host "Public release build."
  $builderNumber = "$builderNumber ($($env:BUILD_COUNTER))"
}

Write-Host "Site extension version: $version"
Write-Host "##vso[build.updatebuildnumber]$builderNumber"