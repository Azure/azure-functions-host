param(
    [string] $Version = $null,
    [string[]] $Artifacts = $null,
    [string] $CommitId = $null,
    [string] $OutputPath
)

if (-not $Version) {
    $Version = dotnet build $PSScriptRoot --getProperty:Version
}

if (-Not $Artifacts) {
    $Artifacts = @(
        "Functions.$Version"
        "Functions.Symbols.$Version.win-x64"
        "Functions.Symbols.$Version.win-x86"
    )
}

if (-not $CommitId) {
    $CommitId = $env:BUILD_SOURCEVERSION
}

if (-not $CommitId) {
    $CommitId = (git rev-parse HEAD).Trim()
}

$obj = @{
    name = $Version
    artifacts = $Artifacts
    tag = "v$Version"
    commitId = $CommitId
    releaseNotesFile = "release_notes.md"
}

if ($OutputPath) {
    Write-Host "Writing $obj to $OutputPath"
    $obj | ConvertTo-Json | Out-File $OutputPath
}

return $obj
