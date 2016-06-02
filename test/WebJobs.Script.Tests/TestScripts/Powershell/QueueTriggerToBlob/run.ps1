$in = Get-Content $inputData
$message = "PowerShell script processed queue message '$in'";
echo $message;
$in | Out-File -Encoding Ascii $output