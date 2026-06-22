### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- fix: address config reload concurrent read/write race (#11815)
- Add additive platform notification for sync triggers under `FUNCTIONS_NOTIFY_PLATFORM_ON_SYNC=true` (#11813)
- fix: avoid health checks triggering secret-manager too early (#11816)
- Restrict GET admin/host/triggers to platform claim (#11697)
- fix: `SetProcessCountToNumberOfCpuCores` silently overriding `MaxProcessCount` on high core count machines (#11842)
