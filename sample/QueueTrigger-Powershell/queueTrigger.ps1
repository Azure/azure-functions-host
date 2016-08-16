$in = Get-Content $triggerInput
$json = $in | ConvertFrom-Json

Write-Output "PowerShell script processed queue message '$json'"

$entity = [string]::Format('{{ "Status": 0, "Title": "PowerShell Table Entity for message {0}" }}', $json.id)
$entity | Out-File -Encoding Ascii $output