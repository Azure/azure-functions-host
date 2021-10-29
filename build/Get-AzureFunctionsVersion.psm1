function Get-AzureFunctionsVersion {
  param(    
    [string] $buildNumber,
    [string] $suffix
  )

  $hasSuffix = ![string]::IsNullOrEmpty($suffix)

  $suffixCmd = ""
  if ($hasSuffix) {
    $suffixCmd = "/p:VersionSuffix=$suffix"
  }

  # use the same logic as the projects to generate the site extension version
  $cmd = "build", "$PSScriptRoot\common.props", "/t:EchoVersion", "-restore:False", "/p:BuildNumber=$buildNumber", $suffixCmd, "--nologo", "-clp:NoSummary"  
  $version = (& dotnet $cmd).Trim()

  return $version
}