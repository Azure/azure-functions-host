function Get-AzureFunctionsVersion {
  param(    
    [string] $buildNumber,
    [string] $suffix,
    [string] $minorVersionPrefix
  )

  $hasSuffix = ![string]::IsNullOrEmpty($suffix)

  $suffixCmd = ""
  if ($hasSuffix) {
    $suffixCmd = "/p:VersionSuffix=$suffix"
  }

  # use the same logic as the projects to generate the site extension version
  $cmd = "build", "$PSScriptRoot\version.proj", "/t:EchoVersion", "-restore:False", "/p:BuildNumber=$buildNumber", "/p:MinorVersionPrefix=$minorVersionPrefix", $suffixCmd, "--nologo", "-clp:NoSummary"  
  $version = (& dotnet $cmd).Trim()

  return $version
}