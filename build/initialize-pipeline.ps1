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

# Get major, minor and patchVersions
[xml]$XMLContents = [xml](Get-Content -Path ".\build\common.props")
$XMLContents.GetElementsByTagName("MajorVersion") |  ForEach-Object {
  $majorVersion = $_.InnerText
  Write-Host "##vso[task.setvariable variable=MajorVersion;isOutput=true]$majorVersion"
  Write-Host "Setting 'MajorVersion' to $majorVersion"
  break
}

$XMLContents.GetElementsByTagName("MinorVersion") |  ForEach-Object {
  $minorVersion = $_.InnerText
  Write-Host "##vso[task.setvariable variable=MinorVersion;isOutput=true]$minorVersion"
  Write-Host "Setting 'MinorVersion' to $minorVersion"
  break
}

$XMLContents.GetElementsByTagName("PatchVersion") |  ForEach-Object {
  $patchVersion = $_.InnerText
  Write-Host "##vso[task.setvariable variable=PatchVersion;isOutput=true]$patchVersion"
  Write-Host "Setting 'PatchVersion' to $patchVersion"
  break
}