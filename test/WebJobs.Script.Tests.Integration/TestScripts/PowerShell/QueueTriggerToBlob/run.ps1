$in = Get-Content $triggerInput
$message = "PowerShell script processed queue message '$in'";
echo $message;
$in | Out-File -Encoding UTF8 $output