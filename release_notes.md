### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Java Worker Version to [2.18.1](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.18.1)
- Introduced support for response compression, which can be enabled through explicit opt-in (#10870)
- Add support for new FeatureFlag `EnableAzureMonitorTimeIsoFormat` to enable iso time format for azmon logs for Linux Dedicated/EP Skus. (#10684)
- Allow sync trigger to happen in managed environment when `AzureWebJobsStorage` is not set (#10767)
- Fixing default DateTime bug with TimeZones in TimerTrigger (#10906)
