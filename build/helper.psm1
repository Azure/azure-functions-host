function AddPatchVersionToCommonProps
{
    param
    (
      [Parameter(Mandatory=$true)]
      [ValidateNotNullOrEmpty()]
      [System.String]
      $FilePath
    )

    if (-not ($FilePath))
    {
      throw "File path '$FilePath' does not exist"
    }

    $fileContent = Get-Content $FilePath -Raw -ErrorAction Stop
    $version = (GetDatePST).ToString("MMdd")
    $patchVersion = '<PatchVersion>' + $version + '</PatchVersion>'
    Write-Host "Set patch version in '$FilePath' to '$patchVersion'"

    $pattern = '<PatchVersion>(.+)</PatchVersion>'
    $newText = $fileContent -replace $pattern, $patchVersion
    Set-Content -Path $FilePath -Value $newText -ErrorAction Stop -Force

    Write-Host "File '$FilePath' updated."
}

# Get the date in Pacific Standard Time
#
function GetDatePST
{
    $now = Get-Date
    $timeZoneInfo  = [TimeZoneInfo]::FindSystemTimeZoneById("Pacific Standard Time")
    $date = [TimeZoneInfo]::ConvertTime($now, $timeZoneInfo)
    return $date
}
