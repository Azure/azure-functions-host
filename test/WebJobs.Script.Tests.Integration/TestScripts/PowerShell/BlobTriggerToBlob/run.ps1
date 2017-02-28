$blob = Get-Content $triggerInput
$message = "PowerShell script processed blob '$blob'";
$blob | Out-File -Encoding Ascii $output