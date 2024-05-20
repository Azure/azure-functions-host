# See if artifacts should be built
if ($env:BUILD_REASON -eq "PullRequest") {

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
