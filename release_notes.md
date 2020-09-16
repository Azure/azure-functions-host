### Release notes
<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Update Python Worker to 1.1.5 [Release Note](https://github.com/Azure/azure-functions-python-worker/releases/tag/1.1.5)
- Update Python Library to 1.3.1 [Release Note](https://github.com/Azure/azure-functions-python-library/releases/tag/1.3.1)
- Add a new host.json property "watchFiles" for restarting the Host when files are modified.
- [CustomHandler][Breaking] If enableForwardingHttpRequest is false, http output binding response is expected to be a valid Json object with following optional fields :
`{
"statusCode" : "",
"status" : "",
"body": "",
"headers" : {}
}`
Exception is thrown if HttpOutputBindingResponse is not valid Json.
- Update PowerShell Worker to 2.0.554 [Release Note](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v2.0.554)

**Release sprint:** Sprint 84
[ [bugs](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+84%22+label%3Abug+is%3Aclosed) | [features](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+84%22+label%3Afeature+is%3Aclosed) ]