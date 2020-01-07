param (
  [string]$connectionString = "",
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

$storageContext = New-AzStorageContext -ConnectionString $connectionString
$blob = Get-AzStorageBlob -Context $storageContext -Container "ci-locks" -Blob $leaseBlob

$accessCondition = New-Object -TypeName Microsoft.Azure.Storage.AccessCondition
$accessCondition.LeaseId = $leaseToken
$blob.ICloudBlob.ReleaseLease($accessCondition)