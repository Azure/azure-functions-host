if ($Env:req_query_name) 
{
	$res = "Hello $Env:req_query_name"
}
else
{
	$res = "Please pass a name on the query string"
}

Out-File -NoNewline $Env:res -InputObject $res