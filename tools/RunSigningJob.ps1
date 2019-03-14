$bypassPackaging = $env:APPVEYOR_PULL_REQUEST_NUMBER -and -not $env:APPVEYOR_PULL_REQUEST_TITLE.Contains("[pack]")
$directoryPath = Split-Path $MyInvocation.MyCommand.Path -Parent

if (-not $bypassPackaging) {
  if ($env:SkipAssemblySigning -eq "true") {
    "Signing disabled. Skipping signing process."
    exit 0;
  }

  # Only sign the ExtensionsMetadataGenerator
  New-Item -ItemType Directory -Force -Path "$directoryPath\..\buildoutput\signing"
  Compress-Archive $directoryPath\..\buildoutput\Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator*.nupkg $directoryPath\..\buildoutput\signing\tosign.zip  
  Remove-Item $directoryPath\..\buildoutput\Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator*.nupkg

  $ctx = New-AzureStorageContext $env:FILES_ACCOUNT_NAME $env:FILES_ACCOUNT_KEY
  Set-AzureStorageBlobContent "$directoryPath/../buildoutput/signing/tosign.zip" "azure-functions-host" -Blob "$env:APPVEYOR_BUILD_VERSION.zip" -Context $ctx

  $queue = Get-AzureStorageQueue "signing-jobs" -Context $ctx

  $messageBody = "SignNupkgs;azure-functions-host;$env:APPVEYOR_BUILD_VERSION.zip"
  $queue.CloudQueue.AddMessage($messageBody)
}