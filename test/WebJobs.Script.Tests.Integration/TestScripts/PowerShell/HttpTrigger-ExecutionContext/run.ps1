$content = "FUNCTIONNAME=$EXECUTION_CONTEXT_FUNCTIONNAME,FUNCTIONDIRECTORY=$EXECUTION_CONTEXT_FUNCTIONDIRECTORY" 
$result = @{Status = 200; Headers =@{ "content-type" = "text/plain" }; Body = $content} | ConvertTo-Json
Out-File -Encoding Ascii $res -inputObject $result;