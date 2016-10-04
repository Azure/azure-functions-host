$html = "<HEAD><TITLE>Azure Functions!!!</TITLE></HEAD>"
$result = [string]::Format('{{ "Status": 200, "Body": "{0}", "Headers": {{ "content-type": "text/html" }} }}', $html)
Out-File -Encoding Ascii $res -inputObject $result;
