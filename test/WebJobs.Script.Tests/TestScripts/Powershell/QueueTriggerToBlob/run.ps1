$in = [Console]::ReadLine()

[Console]::WriteLine("Powershell script processed queue message '$in'")

$output = $Env:output
$in | Out-File -Encoding Ascii $output