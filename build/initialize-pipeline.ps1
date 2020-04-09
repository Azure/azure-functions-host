$buildReason = $env:BUILD_REASON
$sourceBranch = $env:BUILD_SOURCEBRANCH
$bypassPackaging = $true
$suffix = "-ci"
Write-Host "SourceBranch: $sourceBranch, Build reason: $buildReason"

if($sourceBranch.endsWith('release/2.0')) {
  $suffix = ""
}

if(($sourceBranch.endsWith('v2.x') -or $sourceBranch.endsWith('release/2.0')) -and ($buildReason -ne "PullRequest"))
{
  $bypassPackaging = $false
}
elseif($buildReason -eq "PullRequest")
{
  $response = Invoke-RestMethod api.github.com/repos/$env:BUILD_REPOSITORY_ID/pulls/$env:SYSTEM_PULLREQUEST_PULLREQUESTNUMBER
  $title = $response.title.ToLowerInvariant()
  if ($title.Contains("[pack]")) {
    $bypassPackaging = $false
  }
}

Write-Host "BypassPackaging: $bypassPackaging, Suffix: $suffix"

# Write to output
"##vso[task.setvariable variable=Suffix;isOutput=true]$suffix"
"##vso[task.setvariable variable=BypassPackaging;isOutput=true]$bypassPackaging"