param (
  [string]$buildNumber = "0"
)

# Get major.minorVersion
[xml]$XMLContents = [xml](Get-Content -Path ".\build\common.props")
$XMLContents.GetElementsByTagName("MajorMinorProductVersion") |  ForEach-Object {
  $majorMinorVersion = $_.InnerText
  Write-Host "##vso[task.setvariable variable=MajorMinorVersion;isOutput=true]$majorMinorVersion"
  Write-Host "Setting 'MajorMinorVersion' to $majorMinorVersion"
  break
}

Write-Host "Setting the name of the build to '$majorMinorVersion'."
Write-Host "##vso[build.updatebuildnumber]$majorMinorVersion.$buildNumber"