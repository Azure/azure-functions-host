$in = [Console]::ReadLine()

[Console]::WriteLine("Powershell script processed queue message '$in'")

$output = $Env:output
$json = $in | ConvertFrom-Json
$entity = [string]::Format('{{ "timestamp": "{0}", "status": 0, "title": "Powershell Table Entity for message {1}" }}', $(get-date -f MM-dd-yyyy_HH_mm_ss), $json.id)
$entity | Out-File -Encoding Ascii $output