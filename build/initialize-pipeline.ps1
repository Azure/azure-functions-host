$buildReason = $env:BUILD_REASON

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