$in = Get-Content $Env:input

[Console]::WriteLine("Powershell script processed queue message '$in'")

$output = $Env:output
$in | Out-File -Encoding Ascii $output