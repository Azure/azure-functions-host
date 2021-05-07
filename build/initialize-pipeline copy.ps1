# Get major, minor and patchVersions
[xml]$XMLContents = [xml](Get-Content -Path ".\common.props")
$XMLContents.GetElementsByTagName("MajorVersion") |  ForEach-Object {
  $majorVersion = $_.InnerText
  Write-Host "##vso[task.setvariable variable=MajorVersion;isOutput=true]$majorVersion"
  Write-Host "Setting 'MajorVersion' to $majorVersion"
}

$XMLContents.GetElementsByTagName("MinorVersion") |  ForEach-Object {
  $minorVersion = $_.InnerText
  Write-Host "##vso[task.setvariable variable=MinorVersion;isOutput=true]$minorVersion"
  Write-Host "Setting 'MinorVersion' to $minorVersion"
}

$XMLContents.GetElementsByTagName("PatchVersion") |  ForEach-Object {
  $patchVersion = $_.InnerText
  Write-Host "##vso[task.setvariable variable=PatchVersion;isOutput=true]$patchVersion"
  Write-Host "Setting 'PatchVersion' to $patchVersion"
}