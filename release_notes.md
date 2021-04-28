### Release notes
<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Added a feature flag to opt out of the default behavior where the host sets the environment name to `Development` when running in debug mode. To disable the behavior, set the app setting: `AzureWebJobsFeatureFlags` to `DisableDevModeInDebug`
- Reorder CORS and EasyAuth middleware to prevent EasyAuth from blocking CORS requests (#7315)

**Release sprint:** Sprint 100
[ [bugs](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+100%22+label%3Abug+is%3Aclosed) | [features](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+100%22+label%3Afeature+is%3Aclosed) ]
- Update App Service Authentication/Authorization on Linux Consumption from 1.4.0 to 1.4.5. Release notes for this feature captured at https://github.com/Azure/app-service-announcements/issues. (#7205)