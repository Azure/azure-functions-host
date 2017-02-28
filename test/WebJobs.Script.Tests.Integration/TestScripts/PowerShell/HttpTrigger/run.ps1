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

$body = (New-Object PSObject |
   Add-Member -PassThru NoteProperty message $message |
   Add-Member -PassThru NoteProperty {user-agent} ${req_headers_user-agent} |
   Add-Member -PassThru NoteProperty {custom-1} ${req_headers_custom-1}
)

$responseContent = @{result=$body; headers=@{"TEST-HEADER"="Test Response Header"}} | ConvertTo-Json -Compress;
Out-File -Encoding Ascii $res -inputObject $responseContent;