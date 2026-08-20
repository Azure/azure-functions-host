param (
  [string]$leaseBlob = "",
  [string]$leaseToken = ""
)

if ($leaseBlob -eq "") {
  Write-Host "leaseBlob was not specified."
  exit 1
}

if ($leaseToken -eq "") {
  Write-Host "leaseToken was not specified."
  exit 1
}

Write-Host "Breaking lease for $leaseBlob."

Import-Module Az.Storage

$storageContext = New-AzStorageContext -StorageAccountName "azurefunctionshostci0" -UseConnectedAccount
$blob = Get-AzStorageBlob -Context $storageContext -Container "ci-locks" -Blob $leaseBlob

$leaseClient = New-Object Azure.Storage.Blobs.Specialized.BlobLeaseClient($blob.BlobBaseClient, $leaseToken)
$leaseClient.Release() | Out-Null
