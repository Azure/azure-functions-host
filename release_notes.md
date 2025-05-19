### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Memory allocation optimizations in `ScriptStartupTypeLocator.GetExtensionsStartupTypesAsync` (#11012)
- Fix invocation timeout when incoming request contains "x-ms-invocation-id" header (#10980)
- Warn if .azurefunctions folder does not exist (#10967)
- Memory allocation & CPU optimizations in `GrpcMessageExtensionUtilities.ConvertFromHttpMessageToExpando` (#11054)
- Memory allocation optimizations in `ReadLanguageWorkerFile` by reading files in buffered chunks, preventing LOH allocations (#11069)
