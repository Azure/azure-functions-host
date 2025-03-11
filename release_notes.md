### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Java Worker Version to [2.18.1](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.18.1)
- Add support for new FeatureFlag `EnableAzureMonitorTimeIsoFormat` to enable iso time format for azmon logs for Linux Dedicated/EP Skus. (part of #7864)
- Update PowerShell worker to 4.0.4175 (sets defaultRuntimeVersion to 7.4 in worker.config.json)
- Fixing default DateTime bug with TimeZones in TimerTrigger (#10906)
- Add support for the release channel setting `WEBSITE_PlatformReleaseChannel` and use this value in extension bundles resolution.
- Bug fix for platform release channel bundles resolution casing issue and additional logging (#10921)
