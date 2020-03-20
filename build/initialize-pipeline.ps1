$buildReason = $env:BUILD_REASON
$sourceBranch = $env:BUILD_SOURCEBRANCH
$bypassPackaging = $true
$includeSuffix = $true
Write-Host "SourceBranch: $sourceBranch, Build reason: $buildReason"

if($sourceBranch.endsWith('master') -and ($buildReason -ne "PullRequest"))
{
  $includeSuffix = $false
  $bypassPackaging = $false
}
elseif($buildReason -eq "PullRequest")
{
  $response = Invoke-RestMethod api.github.com/repos/$env:BUILD_REPOSITORY_ID/pulls/$env:SYSTEM_PULLREQUEST_PULLREQUESTNUMBER
  $title = $response.title.ToLowerInvariant()
  Write-Host "Title: $title"
  if ($title.Contains("[pack]")) {
    $bypassPackaging = $false
  }
}

Write-Host "bypassPackaging: $bypassPackaging"

# Write to output
"##vso[task.setvariable variable=IncludeSuffix;isOutput=true]$includeSuffix"
"##vso[task.setvariable variable=BypassPackaging;isOutput=true]$bypassPackaging"