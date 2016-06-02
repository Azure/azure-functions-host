$in = Get-Content $inputData
$message = "PowerShell script processed blob message '$in'";
$in | Out-File -Encoding Ascii $output