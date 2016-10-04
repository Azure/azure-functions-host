Write-Output "PowerShell HTTP function invoked"
$msg = [string]::Format("Name: {0}, Category: {1}, Id:{2}", $req_query_name, $req_params_category, $req_params_id)
[io.file]::WriteAllText($res, $msg)