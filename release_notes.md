### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Improved memory metrics reporting using CGroup data for Linux consumption (#10968)
- Memory allocation optimizations in `RpcWorkerConfigFactory.AddProviders` (#10959)
- Fixing GrpcWorkerChannel concurrency bug (#10998)
- Avoid circular dependency when resolving LinuxContainerLegionMetricsPublisher. (#10991)
- Add 'unix' to the list of runtimes kept when importing PowerShell worker for Linux builds (#11006)
- Update PowerShell 7.4 worker to 4.0.4206 (#11006)
- Add `win-arm64` and `linux-arm64` to the list of PowerShell runtimes; added filter for `osx` RIDs (includes `osx-x64` and `osx-arm64`) (#11013)
