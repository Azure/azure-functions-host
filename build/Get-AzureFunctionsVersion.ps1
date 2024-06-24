# Get major, minor and patchVersions
$version = @{}
[xml]$XMLContents = [xml](Get-Content -Path "$PSScriptRoot\common.props")
$XMLContents.GetElementsByTagName("MajorVersion") |  ForEach-Object {
  $version.Major = $_.InnerText
}

$XMLContents.GetElementsByTagName("MinorVersion") |  ForEach-Object {
  $version.Minor = $_.InnerText
}

$XMLContents.GetElementsByTagName("PatchVersion") |  ForEach-Object {
  $version.Patch = $_.InnerText
}

return $version
