### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update PS 7.6 worker to [v4.0.5641](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5641) (#12197)
- Replace NUL characters in system log details, summaries, and exception messages with `<NUL>` to prevent field-shifted rows in log ingestion (#12199)
### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Refactor functions worker runtime retrieval to use `IWorkerRuntimeResolver` abstraction (#11511)
- Suppress EventHub and Storage queue trigger polling noise from telemetry (#11603)
- Add check for empty worker tag propagation to improve performance (#11656)
- Update Python Worker Version to [4.44.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.44.0) (#11668)
- Install extension bundles atomically with a completion marker and repair incomplete downloaded bundles (#11326)
