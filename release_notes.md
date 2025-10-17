### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Emit diagnostic warning for deprecated Azure Functions Proxies usage (#11405)
- Update Python Worker Version to [4.40.2](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.40.2)
- Add JitTrace Files for v4.1044
- Send `TelemetryHealthCheckPublisher` logs to ScriptHost `ILogger` (#11398)
- Implementing a resolver that resolves worker configurations from specified probing paths (#11258)
- Remove duplicate function names from sync triggers payload(#11371)
- Avoid emitting empty tag values for health check metrics (#11393)
- Run health checks from the active ScriptHost (#11410)
- Publish health check metrics to legacy AppInsights SDK (#11365)
- Fix tag filter for health check live & ready endpoints (#11363)
- Functions host to take a customer specified port in Custom Handler scenario (#11408)
- Updated to version 1.5.8 of Microsoft.Azure.AppService.Middleware.Functions (#11416)
- Enabling worker indexing for Logic Apps app kind behind an enviornment setting
- Adding HttpWorkerFunctionProvider functions to synctrigger payload (#11430)
- Remove the Flex-only AzureMonitorDiagnosticLoggerProvider filter (#11431)