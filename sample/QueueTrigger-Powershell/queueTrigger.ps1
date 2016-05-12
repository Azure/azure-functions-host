$in = Get-Content $Env:input

[Console]::WriteLine("Powershell script processed queue message '$in'")

$output = $Env:output
$json = $in | ConvertFrom-Json
$entity = [string]::Format('{{ "Status": 0, "Title": "Powershell Table Entity for message {0}" }}', $json.id)
$entity | Out-File -Encoding Ascii $output