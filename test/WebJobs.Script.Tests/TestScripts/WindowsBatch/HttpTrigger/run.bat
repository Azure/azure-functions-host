echo OFF

IF DEFINED req_headers_test-header (
	echo test-header = %req_headers_test-header% >> %res%
)

IF DEFINED req_query_value (
	echo Value = %req_query_value% >> %res%
) ELSE (
	echo Please pass a value on the query string >> %res%
)