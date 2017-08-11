$isPr = Test-Path env:APPVEYOR_PULL_REQUEST_NUMBER

if (-not $isPr) {
  $ctx = New-AzureStorageContext $env:FILES_ACCOUNT_NAME $env:FILES_ACCOUNT_KEY
  Set-AzureStorageBlobContent "$PSScriptRoot/../bin/tosign.zip" "webjobs" -Blob "$env:APPVEYOR_BUILD_VERSION.zip" -Context $ctx

  $queue = Get-AzureStorageQueue "signing-jobs" -Context $ctx

  $messageBody = "Sign;webjobs;$env:APPVEYOR_BUILD_VERSION.zip"
  $message = New-Object -TypeName Microsoft.WindowsAzure.Storage.Queue.CloudQueueMessage -ArgumentList $messageBody
  $queue.CloudQueue.AddMessage($message)
}