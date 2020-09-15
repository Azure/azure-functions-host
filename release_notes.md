### Release notes
<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Python Worker to 1.1.5 [Release Note](https://github.com/Azure/azure-functions-python-worker/releases/tag/1.1.5)
- Update Python Library to 1.3.1 [Release Note](https://github.com/Azure/azure-functions-python-library/releases/tag/1.3.1)
- **[BreakingChange]**[CustomHandler] If enableForwardingHttpRequest is false, http output binding response is expected to be a valid Json object with following optional fields :
`{
"statusCode" : "",
"status" : "",
"body": "",
"headers" : {}
}`
Exception is thrown if HttpOutputBindingResponse is not valid Json.
- **[BreakingChange]**[CustomHandler]Send query params as JObject and Identities as JArray PR #6621

- Update Java Worker to 1.8.0 [Release Note](https://github.com/Azure/azure-functions-java-worker/releases/tag/1.8.0)
- Update Java Library to 1.4.0 [Release Note](https://github.com/Azure/azure-functions-java-library)
- **[BreakingChange]** Fixes [#400](https://github.com/Azure/azure-functions-java-worker/issues/400) which was a regression from the 1.7.1 release.
There is potential of impact if the function code has taken a dependency on a feature in gson 2.8.6 as the dependency `gson-2.8.5.jar` is now included in the class path of the worker and will take precedence over the function's lib folder.

**Release sprint:** Sprint 84
[ [bugs](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+84%22+label%3Abug+is%3Aclosed) | [features](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+84%22+label%3Afeature+is%3Aclosed) ]
