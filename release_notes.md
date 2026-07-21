### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Fixed a WebHost worker channel shutdown race that could hang host teardown when a language worker was still initializing. (#11853)
- Fixed a race where a SyncTriggers request during specialization could publish placeholder (`WarmUp`) triggers. (#11874)
- Update Python Worker Version to [4.45.1](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.45.1) (#11860)
- Update Microsoft.Azure.AppService.Middleware to 1.5.11 (#11866)
- Update PS 7.4 worker to [v4.0.5212](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5212) (#11858)
- Update PS 7.6 worker to [v4.0.5213](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5213) (#11858)
- Update `Microsoft.Azure.Functions.DotNetIsolatedNativeHost` to `1.0.15` (#11881)
- Update Python Worker Version to [4.46.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.46.0) (#11885)
