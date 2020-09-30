### Release notes
<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Update Python Worker to 1.1.5 [Release Note](https://github.com/Azure/azure-functions-python-worker/releases/tag/1.1.5)
- Update Python Library to 1.3.1 [Release Note](https://github.com/Azure/azure-functions-python-library/releases/tag/1.3.1)
- Add a new host.json property "watchFiles" for restarting the Host when files are modified.
- Update Java Worker to 1.8.0 [Release Note](https://github.com/Azure/azure-functions-java-worker/releases/tag/1.8.0)
- Update Java Library to 1.4.0 [Release Note](https://github.com/Azure/azure-functions-java-library)
- Added support for function execution retry on invocation failures [#6664](https://github.com/Azure/azure-functions-host/issues/6664)
- **[BreakingChange]** Fixes [#400](https://github.com/Azure/azure-functions-java-worker/issues/400) which was a regression from the 1.7.1 release.
   There is potential of impact if the function code has taken a dependency on a feature in gson 2.8.6 as the dependency `gson-2.8.5.jar` is now included in the class path of the worker and will take precedence over the function's lib folder.
- **Breaking Changes in CustomHandler**
    -  Issue [#6644](https://github.com/Azure/azure-functions-host/issues/6644) : If enableForwardingHttpRequest is false, http output binding response is expected to be a valid Json object with following optional fields :
    ```json
    {
    "statusCode" : "",
    "status" : "",
    "body": "",
    "headers" : {}
    }
    ```
    Exception is thrown if HttpOutputBindingResponse is not valid Json.
    - We identified a couple of inconsistencies in the request schema and next release will include following changes.
        - Issue [#6606](https://github.com/Azure/azure-functions-host/issues/6606): 
            - Query property will change from a JSON serialized string to a dictionary
            - Identities property will change from a JSON serialized string to an array
        - Issue [#6574](https://github.com/Azure/azure-functions-host/issues/6574)
            - Metadata / Input binding data of type DateTime will not be serialzed as string
    If you are using these properties, please ensure your app is able to detect and handle the new schema.

**Release sprint:** Sprint 84
[ [bugs](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+84%22+label%3Abug+is%3Aclosed) | [features](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+84%22+label%3Afeature+is%3Aclosed) ]

**Release sprint:** Sprint 85
[ [bugs](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+85%22+label%3Abug+is%3Aclosed) | [features](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+85%22+label%3Afeature+is%3Aclosed) ]