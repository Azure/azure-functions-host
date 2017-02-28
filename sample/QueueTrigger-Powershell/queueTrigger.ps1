$in = Get-Content $triggerInput
$json = $in | ConvertFrom-Json

Write-Output "PowerShell script processed queue message '$json'"

$title = [string]::Format("PowerShell Table Entity for message {0}", $json.id)
$entity = [PSObject]@{
  Status = 0
  Title = $title
}
$entity = $entity | ConvertTo-Json
$entity | Out-File -Encoding UTF8 $output