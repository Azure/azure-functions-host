function AcquireLease($blob) {
  try {
    return $blob.ICloudBlob.AcquireLease($null, $null, $null, $null, $null)
  } catch {
    Write-Host "  Error: $_"
    return $null
  } 
}

# use this for tracking metadata in lease blobs
$buildName = "$env:BUILD_BUILDID - $env:BUILD_BUILDNUMBER ($env:SYSTEM_JOBDISPLAYNAME)"
$buildUrl = "$env:SYSTEM_TEAMFOUNDATIONCOLLECTIONURI$env:SYSTEM_TEAMPROJECT/_build/results?buildId=$env:BUILD_BUILDID"

Import-Module Az.Storage

# get a blob lease to prevent test overlap
$storageContext = New-AzStorageContext -StorageAccountName "azurefunctionshostci0" -UseConnectedAccount

While($true) {
  $blobs = Get-AzStorageBlob -Context $storageContext -Container "ci-locks"
  $token = $null
  
  # shuffle the blobs for random ordering
  $blobs = $blobs | Sort-Object {Get-Random}

  Write-Host "Looking for unleased ci-lock blobs (list is shuffled):"
  Foreach ($blob in $blobs) {
    $name = $blob.Name
    $leaseStatus = $blob.ICloudBlob.Properties.LeaseStatus
    
    Write-Host "  ${name}: $leaseStatus"
    
    if ($leaseStatus -eq "Locked") {
      try {
        $blob.ICloudBlob.FetchAttributes()
        $lastModified = $blob.ICloudBlob.Properties.LastModified
        if ($lastModified -ne $null -and $lastModified.UtcDateTime -lt (Get-Date).AddHours(-6).ToUniversalTime()) {
          $age = [math]::Round(((Get-Date).ToUniversalTime() - $lastModified.UtcDateTime).TotalHours, 1)
          $build = $blob.ICloudBlob.Metadata["Build"]
          $url = $blob.ICloudBlob.Metadata["BuildUrl"]
          Write-Host "##vso[task.logissue type=warning]Stale lease detected on '${name}' (locked for ${age}h). Build: ${build} | URL: ${url}"
          Write-Host "  Breaking stale lease on ${name}."
          $blob.ICloudBlob.BreakLease([TimeSpan]::Zero, $null, $null, $null)
          Write-Host "  Lease broken. Will attempt to acquire on next pass."
        }
      } catch {
        Write-Host "##vso[task.logissue type=warning]Unable to inspect or break stale lease on '${name}'. Continuing. $($_.Exception.Message)"
      }
      continue
    }

    Write-Host "  Attempting to acquire lease on $name."
    $token = AcquireLease $blob
    if ($token -ne $null) {
      Write-Host "  Lease acquired on $name. LeaseId: '$token'"
      Write-Host "##vso[task.setvariable variable=LeaseBlob]$name"
      Write-Host "##vso[task.setvariable variable=LeaseToken]$token"
      try {
        $blob.ICloudBlob.FetchAttributes()
        $blob.ICloudBlob.Metadata["Build"] = $buildName
        $blob.ICloudBlob.Metadata["BuildUrl"] = $buildUrl
        $accessCondition = New-Object -TypeName Microsoft.Azure.Storage.AccessCondition
        $accessCondition.LeaseId = $token
        $blob.ICloudBlob.SetMetadata($accessCondition)
      } catch {
        # best effort
        Write-Host "Warning: unable to update blob metadata. Continuing. $_"
      }
      break
    } else {
      Write-Host "  Lease not acquired on $name."
    }    
  }
  
  if ($token -ne $null) {
    break
  }
  
  $delay = 30
  Write-Host "No lease acquired. Waiting $delay seconds to try again. This run cannot begin until it acquires a lease on a CI test environment."
  Start-Sleep -s $delay
  Write-Host ""
}