try
{
	$requestBody = Get-Content $req.body -Raw | ConvertFrom-Json;
}
catch
{
	$requestBody = Get-Content $req -Raw
}
 
if ($req_query_name) 
{
    $name = $req_query_name;
    $message = "Hello $name"; 
}
else
{
    $message = $requestBody;
}

$responseContent = @{reqBody=$message; headers=@{"TEST-HEADER"="Test Response Header"}} | ConvertTo-Json -Compress;
Out-File -Encoding Ascii $res -inputObject $responseContent;