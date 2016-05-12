$in = Get-Content $inputData
$json = $in | ConvertFrom-Json
$entity = [string]::Format('{{ "Status": 0, "Title": "Powershell Table Entity for message {0}" }}', $json.id)
$entity | Out-File -Encoding Ascii $output