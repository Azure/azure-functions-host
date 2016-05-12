$in = Get-Content $inputData
$message = "Powershell script processed queue message '$in'";
echo $message;
$in | Out-File -Encoding Ascii $output