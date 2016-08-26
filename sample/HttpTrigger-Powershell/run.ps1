if ($req_query_name) 
{
	$message = "Hello $req_query_name"
}
else
{
	$message = "Please pass a name on the query string"
}

[io.file]::WriteAllText($res, $message)