### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Refactor functions worker runtime retrieval to use `IWorkerRuntimeResolver` abstraction (#11511)
- Suppress EventHub and Storage queue trigger polling noise from telemetry (#11603)
- Add check for empty worker tag propagation to improve performance (#11656)
