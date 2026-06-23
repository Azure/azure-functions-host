### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Bug-Fix: Fail in-flight invocations when ScriptHost restarts (#11810)
- fix: address config reload concurrent read/write race (#11815)
- Add additive platform notification for sync triggers under `FUNCTIONS_NOTIFY_PLATFORM_ON_SYNC=true` (#11813)
- fix: avoid health checks triggering secret-manager too early (#11816)
- Restrict GET admin/host/triggers to platform claim (#11697)
- fix: `SetProcessCountToNumberOfCpuCores` silently overriding `MaxProcessCount` on high core count machines (#11842)
- Update Python Worker Version to [4.45.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.45.0) (#11850)
