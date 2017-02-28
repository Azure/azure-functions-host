echo OFF

IF DEFINED req_query_details (
	echo URL = "%req_original_url%" >> %res%
	echo Query = "%req_query%" >> %res%
)

IF DEFINED req_headers_test-header (
	echo test-header = %req_headers_test-header% >> %res%
)

SET /p req=<%req%
IF "%req_method%" == "POST" (
	echo Body = %req% >> %res%
)

IF DEFINED req_query_value (
	echo Value = %req_query_value% >> %res%
) ELSE (
	echo Please pass a value on the query string >> %res%
)