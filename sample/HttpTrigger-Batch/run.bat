echo OFF

echo Batch HTTP function invoked!

echo Identity: (%IDENTITY_AUTHENTICATION_TYPE%, %IDENTITY_CLAIMS_AUTHLEVEL%, %IDENTITY_CLAIMS_KEYID%)

IF DEFINED req_query_name (
	echo Hello %REQ_QUERY_NAME%! > %res%
) ELSE (
	echo Please pass a name on the query string > %res%
)