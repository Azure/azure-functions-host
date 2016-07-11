$in = Get-Content $triggerInput
$json = $in | ConvertFrom-Json
$entity = [string]::Format('{{ "Status": 0, "Title": "PowerShell Table Entity for message {0}" }}', $json.id)
$entity | Out-File -Encoding Ascii $output