echo OFF

echo Batch HTTP function invoked!

IF DEFINED req_query_name (
	echo Hello %req_query_name%! > %res%
) ELSE (
	echo Please pass a name on the query string > %res%
)