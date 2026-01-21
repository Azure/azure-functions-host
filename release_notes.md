### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Add JitTrace Files for v4.1046
- Update Java Worker Version to [2.19.4](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.19.4)
- Adding support for OTEL_SERVICE_NAME and OTEL_RESOURCE_ATTRIBUTES env variable (#11506)
- Tagging cold start impacted requests (#11542)
- Stop collecting telemetry from admin endpoint requests (#11544)
- Add area & error code to script & web host health checks (#11552)
- Log siteUpdateId during initialization to track rolling update progress (#11527)
- Update `Microsoft.Azure.Functions.DotNetIsolatedNativeHost` to `1.0.14` (#11569)
    - [Remove net6.0 prelaunch app.](https://github.com/Azure/azure-functions-dotnet-worker/pull/3299)
    - [Add net10.0 prelaunch app.](https://github.com/Azure/azure-functions-dotnet-worker/pull/3297)
    - [Build Linux artifacts using ubuntu 24.](https://github.com/Azure/azure-functions-dotnet-worker/pull/3285)
- Update Python Worker Version to [4.42.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.42.0)
