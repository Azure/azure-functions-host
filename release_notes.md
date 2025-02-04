### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Add support for managed identity when using open telemetry + azure monitor (#10615)
- Update Java Worker Version to [2.18.0](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.18.0)
- Allow for an output binding value of an invocation result to be null (#10698)
- Updated dotnet-isolated worker to 1.0.12.
  - [Corrected the path for the prelaunch app location.](https://github.com/Azure/azure-functions-dotnet-worker/pull/2897)
  - [Added net9 prelaunch app.](https://github.com/Azure/azure-functions-dotnet-worker/pull/2898)
- Update the `DefaultHttpProxyService` to better handle client disconnect scenarios (#10688)
  - Replaced `InvalidOperationException` with `HttpForwardingException` when there is a ForwarderError
- Fix modification of ISO strings during the parsing of function metadata bindings.
  - This fixes the listener errors related to the CosmosDB trigger's `StartFromTime` parameter. (#10735)
- Updated `WebJobs.Script` to target .NET 8 (instead of .NET Standard 2.1)
