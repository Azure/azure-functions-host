### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- fix: address config reload concurrent read/write race (#11815)
- fix: avoid health checks triggering secret-manager too early (#11816)
- Restore Workflows-bundle worker discovery on Logic App (#11759)
- Ensure wwwroot directory exists on new slot and app creation w/ networking restrictions (#11757)
- Update Node.js Worker Version to [3.14.1](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v3.14.1) (#PR)
- Log enum names instead of integers in options formatting for `TimerTriggerPlatformOptions` (#11689)
- Update WebHostWorkerRuntimeResolver to always read from IConfiguration variable value for FWR (#11720)
- Restrict GET admin/host/triggers to platform claim (#11697)