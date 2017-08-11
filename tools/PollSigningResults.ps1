$isPr = Test-Path env:APPVEYOR_PULL_REQUEST_NUMBER

if (-not $isPr) {
  $timeout = new-timespan -Minutes 1
  $sw = [diagnostics.stopwatch]::StartNew();
  $polling = $true;
  $ctx = New-AzureStorageContext $env:FILES_ACCOUNT_NAME $env:FILES_ACCOUNT_KEY
  $blob = $null;
  while ($sw.elapsed -lt $timeout -and $polling) {
    $blob = Get-AzureStorageBlob "$env:APPVEYOR_BUILD_VERSION.zip" "webjobs-signed" -Context $ctx -ErrorAction Ignore
    if (-not $blob) {
      "${sw.elapsed} elapsed, polling..."
    }
    else {
      "Jenkins artifacts found"
      $polling = $false;
    }
    Start-Sleep -Seconds 5
  }

  if ($polling) {
    "No jenkins artifacts found, investigate job at https://zenkins.redmond.corp.microsoft.com/view/Functions/job/Functions_signing/"
    exit(1);
  }
  Remove-Item "$PSScriptRoot/../bin/sign" -Recurse -Force
  Remove-Item "$PSScriptRoot/../bin/signed.zip" -Recurse -Force

  Get-AzureStorageBlobContent "$env:APPVEYOR_BUILD_VERSION.zip" "webjobs-signed" -Destination "$PSScriptRoot/../bin/signed.zip" -Context $ctx

  Expand-Archive "$PSScriptRoot/../bin/signed.zip" "$PSScriptRoot/../bin/sign"

  MSBuild.exe "$PSScriptRoot/../WebJobs.proj" /t:ReplaceDlls /p:Configuration=Release

  MSBuild.exe "$PSScriptRoot/../src/Packages/Packages.csproj" /t:Rebuild "/p:Configuration=Release;BUILD_NUMBER=$env:APPVEYOR_BUILD_VERSION;OutputPath=$PSScriptRoot/../bin/Packages;RunCodeAnalysis=false"

  Get-ChildItem "$PSScriptRoot/../bin/PackagesNuGet" | % {
    Push-AppveyorArtifact $_.FullName
  }
}