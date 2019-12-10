param (
  [string]$connectionString = ""
)

function AcquireLease($blob) {
  try {
    return $blob.ICloudBlob.AcquireLease($null, $null, $null, $null, $null)    
  } catch {
    Write-Host "  Error: $_"
    return $null
  } 
}

# parse PR title to see if we should pack this
$response = Invoke-RestMethod api.github.com/repos/$env:BUILD_REPOSITORY_ID/pulls/$env:SYSTEM_PULLREQUEST_PULLREQUESTNUMBER
$title = $response.title.ToLowerInvariant()
Write-Host "Pull request '$title'"
if ($title.Contains("[pack]")) {
  Write-Host "##vso[task.setvariable variable=BuildArtifacts;isOutput=true]true"
  Write-Host "Setting 'BuildArtifacts' to true."
}

# set the build suffix
$branchName = $env:BUILD_SOURCEBRANCHNAME
$buildReason = $env:BUILD_REASON
$buildNumber = $env:BUILD_BUILDID
Write-Host "Branch name: '$branchName'"
Write-Host "Build reason: '$buildReason'"
Write-Host "Build number: '$buildNumber'"

$suffix = "ci"
$emgSuffix = "ci$buildNumber" #ExtensionsMetadataGenerator suffix

if ($branchName.StartsWith("releases/")) {
  $suffix = ""
  $emgSuffix = ""
}

Write-Host "##vso[task.setvariable variable=Suffix;isOutput=true]$suffix"
Write-Host "##vso[task.setvariable variable=EmgSuffix;isOutput=true]$emgSuffix"
Write-Host "Setting 'Suffix' to '$suffix'."
Write-Host "Setting 'EmgSuffix' to '$emgSuffix'."

# get a blob lease to prevent test overlap
$storageContext = New-AzureStorageContext -ConnectionString $connectionString
While($true) {
  $blobs = Get-AzureStorageBlob -Context $storageContext -Container "ci-locks"
  $token = $null
  
  # shuffle the blobs for random ordering
  $blobs = $blobs | Sort-Object {Get-Random}

  Write-Host "Looking for unleased ci-lock blobs (list is shuffled):"
  Foreach ($blob in $blobs) {
    $name = $blob.Name
    $leaseStatus = $blob.ICloudBlob.Properties.LeaseStatus
    
    Write-Host "  ${name}: $leaseStatus"
    
    if ($leaseStatus -eq "Locked") {
      continue
    }

    Write-Host "  Attempting to acquire lease on $name."
    $token = AcquireLease $blob
    if ($token -ne $null) {
      Write-Host "  Lease acquired on $name. LeaseId: '$token'"
      Write-Host "##vso[task.setvariable variable=LeaseBlob;isOutput=true]$name"
      Write-Host "##vso[task.setvariable variable=LeaseToken;isOutput=true]$token"
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