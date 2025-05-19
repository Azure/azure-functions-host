### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Python Worker Version to [4.40.2](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.40.2)
- Add JitTrace Files for v4.1044
- Memory allocation optimizations in `ScriptStartupTypeLocator.GetExtensionsStartupTypesAsync` (#11012)
- Fix invocation timeout when incoming request contains "x-ms-invocation-id" header (#10980)
- Throw exception instead of timing out when worker channel exits before initializing gRPC (#10937)
