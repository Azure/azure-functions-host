param (
  [string]$buildNumber
)

$buildReason = $env:BUILD_REASON
$sourceBranch = $env:BUILD_SOURCEBRANCH

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

# Get major, minor and patchVersions
$version = & $PSScriptRoot\Get-AzureFunctionsVersion.ps1
Write-Host "##vso[task.setvariable variable=MajorVersion;isOutput=true]$($version.Major)"
Write-Host "Setting 'MajorVersion' to $($version.Major)"

Write-Host "##vso[task.setvariable variable=MinorVersion;isOutput=true]$($version.Minor)"
Write-Host "Setting 'MinorVersion' to $($version.Minor)"

Write-Host "##vso[task.setvariable variable=PatchVersion;isOutput=true]$($version.Patch)"
Write-Host "Setting 'PatchVersion' to $($version.Patch)"

#Update buildnumber with the same (Will be used by release pipelines)
$customBuildNumber = "$($version.Major).$($version.Minor).$($version.Patch)"
if(($buildReason -eq "PullRequest") -or !($sourceBranch.ToLower().Contains("release")))
{
  $customBuildNumber = "$customBuildNumber-$buildNumber"
}

Write-Host "##vso[build.updatebuildnumber]$customBuildNumber"
