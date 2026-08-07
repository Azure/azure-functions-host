<#
.SYNOPSIS
Regenerates the checked-in compiled public API baselines after an approved API change.

.DESCRIPTION
Normal test runs only compare and fail. This is the only workflow that writes the checked-in
baselines under test/WebJobs.Script.PublicApi.Tests/Baselines.

The script:

  1. resolves the repository root independently of the current working directory;
  2. runs the candidate-writer test in Release;
  3. writes candidates only under out/public-api-candidates;
  4. verifies the candidate assembly, manifest, and baseline sets and their LF/UTF-8 format;
  5. refuses to bless removal or drift of a Core Tools hard-preserve record unless the separately
     maintained contract and its evidence have been changed in the same working tree;
  6. copies the verified candidates over the checked-in baselines;
  7. reruns the normal comparison test with no update mode.

The script never infers success from a test failure, and it never touches the Phase 0 environment
migration baselines.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectDirectory = Join-Path $repositoryRoot "test\WebJobs.Script.PublicApi.Tests"
$testProject = Join-Path $projectDirectory "WebJobs.Script.PublicApi.Tests.csproj"
$manifestPath = Join-Path $projectDirectory "ShippedAssemblyManifest.json"
$contractPath = Join-Path $projectDirectory "CoreToolsCompatibilityContract.json"
$baselineDirectory = Join-Path $projectDirectory "Baselines"
$candidateDirectory = Join-Path $repositoryRoot "out\public-api-candidates"

if (-not (Test-Path $testProject)) {
  throw "Unable to locate '$testProject'."
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$contract = Get-Content $contractPath -Raw | ConvertFrom-Json
$assemblies = @($manifest.packages | ForEach-Object { $_.assemblies })

function Invoke-PublicApiTest {
  param(
    [Parameter(Mandatory = $true)][string] $Filter,
    [Parameter(Mandatory = $true)][string] $Description,
    [hashtable] $Environment = @{}
  )

  $previous = @{}
  foreach ($name in $Environment.Keys) {
    $previous[$name] = [System.Environment]::GetEnvironmentVariable($name)
    [System.Environment]::SetEnvironmentVariable($name, $Environment[$name])
  }

  try {
    Write-Host "==> $Description"
    & dotnet test $testProject -c release --filter $Filter
    if ($LASTEXITCODE -ne 0) {
      throw "$Description failed with exit code $LASTEXITCODE. The baselines were not changed."
    }
  }
  finally {
    foreach ($name in $Environment.Keys) {
      [System.Environment]::SetEnvironmentVariable($name, $previous[$name])
    }
  }
}

Invoke-PublicApiTest `
  -Filter "FullyQualifiedName=Microsoft.Azure.WebJobs.Script.PublicApi.Tests.PublicApiBaselineTests.WriteCandidateBaselines" `
  -Description "Generating candidate baselines" `
  -Environment @{ "UPDATE_PUBLIC_API_BASELINES" = "1"; "PUBLIC_API_BASELINE_DIRECTORY" = $null }

if (-not (Test-Path $candidateDirectory)) {
  throw "The candidate directory '$candidateDirectory' was not created. The baselines were not changed."
}

$expectedFiles = $assemblies | ForEach-Object { Split-Path $_.baselineFile -Leaf }
$actualFiles = Get-ChildItem $candidateDirectory -Filter *.txt | ForEach-Object { $_.Name }

$missing = $expectedFiles | Where-Object { $actualFiles -notcontains $_ }
$unexpected = $actualFiles | Where-Object { $expectedFiles -notcontains $_ }

if ($missing -or $unexpected) {
  throw ("The generated candidate set does not match the shipped assembly manifest. " +
    "Missing: [$($missing -join ', ')]. Unexpected: [$($unexpected -join ', ')]. The baselines were not changed.")
}

foreach ($assembly in $assemblies) {
  $name = Split-Path $assembly.baselineFile -Leaf
  $path = Join-Path $candidateDirectory $name
  $bytes = [System.IO.File]::ReadAllBytes($path)

  if ($bytes.Length -eq 0) {
    throw "Candidate baseline '$name' is empty. The baselines were not changed."
  }

  if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
    throw "Candidate baseline '$name' has a UTF-8 byte order mark. The baselines were not changed."
  }

  if ($bytes -contains 0x0D) {
    throw "Candidate baseline '$name' contains CRLF line endings. The baselines were not changed."
  }

  if ($bytes[$bytes.Length - 1] -ne 0x0A) {
    throw "Candidate baseline '$name' does not end with a newline. The baselines were not changed."
  }

  $content = [System.Text.Encoding]::UTF8.GetString($bytes)
  $records = $content -split "`n" | Where-Object { $_ -ne "" -and -not $_.StartsWith("#") }

  if ($records.Count -eq 0) {
    throw "Candidate baseline '$name' contains no records. The baselines were not changed."
  }

  $identity = $records | Where-Object { $_ -eq "assembly | name | $($assembly.baselineAssemblyName)" }
  if (-not $identity) {
    throw ("Candidate baseline '$name' does not record assembly '$($assembly.baselineAssemblyName)'. " +
      "The baselines were not changed.")
  }
}

# The audited Core Tools records may not be blessed away by this workflow. If a preserve record is
# no longer produced, the contract and its evidence must be changed deliberately in the same tree.
$contractChanged = $false
& git -C $repositoryRoot diff --quiet -- "test/WebJobs.Script.PublicApi.Tests/CoreToolsCompatibilityContract.json"
if ($LASTEXITCODE -ne 0) {
  $contractChanged = $true
}
else {
  & git -C $repositoryRoot diff --cached --quiet -- "test/WebJobs.Script.PublicApi.Tests/CoreToolsCompatibilityContract.json"
  if ($LASTEXITCODE -ne 0) {
    $contractChanged = $true
  }
}

$brokenPreserveRecords = @()

foreach ($record in $contract.preserve) {
  $assembly = $assemblies | Where-Object { $_.baselineAssemblyName -eq $record.assembly }
  if (-not $assembly) {
    throw "The Core Tools contract references unknown assembly '$($record.assembly)'. The baselines were not changed."
  }

  $path = Join-Path $candidateDirectory (Split-Path $assembly.baselineFile -Leaf)
  $expectedLine = "$($record.kind) | $($record.identity) | $($record.signature)"

  if (-not (Select-String -Path $path -SimpleMatch -Pattern $expectedLine -Quiet)) {
    $brokenPreserveRecords += $record.id
  }
}

if ($brokenPreserveRecords.Count -gt 0 -and -not $contractChanged) {
  throw ("The candidate baselines drop or change these Core Tools required records: " +
    "[$($brokenPreserveRecords -join ', ')]. Azure Functions Core Tools 'main' compiles against them. " +
    "Restore them, or update 'test/WebJobs.Script.PublicApi.Tests/CoreToolsCompatibilityContract.json' " +
    "and its evidence as part of a coordinated Core Tools change. The baselines were not changed.")
}

if ($brokenPreserveRecords.Count -gt 0) {
  Write-Warning ("Blessing a Core Tools contract change for: [$($brokenPreserveRecords -join ', ')]. " +
    "This is only valid because CoreToolsCompatibilityContract.json was modified in this working tree.")
}

if (-not (Test-Path $baselineDirectory)) {
  New-Item -ItemType Directory -Path $baselineDirectory | Out-Null
}

Get-ChildItem $baselineDirectory -Filter *.txt |
  Where-Object { $expectedFiles -notcontains $_.Name } |
  ForEach-Object {
    Write-Host "    removing stale baseline $($_.Name)"
    Remove-Item $_.FullName
  }

foreach ($name in $expectedFiles) {
  Copy-Item (Join-Path $candidateDirectory $name) (Join-Path $baselineDirectory $name) -Force
  Write-Host "    updated Baselines/$name"
}

Invoke-PublicApiTest `
  -Filter "FullyQualifiedName~Microsoft.Azure.WebJobs.Script.PublicApi.Tests" `
  -Description "Verifying the refreshed baselines" `
  -Environment @{ "UPDATE_PUBLIC_API_BASELINES" = $null; "PUBLIC_API_BASELINE_DIRECTORY" = $null }

Write-Host ""
Write-Host "Public API baselines refreshed. Review the diff under test/WebJobs.Script.PublicApi.Tests/Baselines before committing."
