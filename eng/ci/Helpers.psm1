function Get-AzureFunctionsVersion {
  # use the same logic as the projects to generate the site extension version
  $cmd = "build", "$PSScriptRoot\Version.proj", "/t:EchoVersion", "--no-restore", "--nologo", "-clp:NoSummary"  
  $version = (& dotnet $cmd).Trim()

  return $version
}

function Get-DirectoryAbove([string] $fileName, [string] $startPath = $null) {
  if (-not $startPath) {
    $startPath = (Get-Location).Path
  }

  while ($true) {
    if (Test-Path "$startPath\$fileName") {
      return $startPath
    }

    $startPath = Split-Path -Parent $startPath

    if (-not $startPath) {
      throw "Could not find $fileName in any parent directory"
    }
  }
}
