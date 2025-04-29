### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Improved memory metrics reporting using CGroup data for Linux consumption (#10968)
- Memory allocation optimizations in `RpcWorkerConfigFactory.AddProviders` (#10959)
- Fixing GrpcWorkerChannel concurrency bug (#10998)
- Avoid circular dependency when resolving LinuxContainerLegionMetricsPublisher. (#10991)
- Add 'unix' to the list of runtimes kept when importing PowerShell worker for Linux builds
- Update PowerShell 7.4 worker to 4.0.4206
- Update Python Worker Version to [4.37.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.37.0)
- Add runtime and process metrics. (#11034)
- Add `win-arm64` and `linux-arm64` to the list of PowerShell runtimes; added filter for `osx` RIDs (includes `osx-x64` and `osx-arm64`) (#11013)
- Disable Diagnostic Events when Table Storage is not accessible (#10996)