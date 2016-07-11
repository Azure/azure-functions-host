$msg = "Hello " + $req_query_name;
echo $msg > $res;
Get-DateToday | Out-String;
Show-Calendar -Start "March, 2016" -End "May, 2016" >> $res;