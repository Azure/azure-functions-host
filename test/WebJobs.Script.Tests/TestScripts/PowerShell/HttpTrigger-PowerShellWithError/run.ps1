$dateObject = Get-DateToday;
$dateMessage = "Today's date is $dateObject";
if ($req_query_name) 
{
    $name = $req_query_name;
    $message = "Hello $name.  Today's date is $dateMessage"; 
}
else
{
    $message = $dateMessage;
}

$responseContent = @{reqBody=$message; headers=@{"TEST-HEADER"="Test Response Header"}} | ConvertTo-Json -Compress;
Out-File -Encoding Ascii $res -inputObject $responseContent;