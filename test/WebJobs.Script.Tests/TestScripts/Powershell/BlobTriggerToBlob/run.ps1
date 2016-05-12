$in = Get-Content $inputData
$message = "Powershell script processed blob message '$in'";
$in | Out-File -Encoding Ascii $output