### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Java Worker Version to [2.18.1](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.18.1)
- Introduced support for response compression, which can be enabled through explicit opt-in (#10870)
- Add support for new FeatureFlag `EnableAzureMonitorTimeIsoFormat` to enable iso time format for azmon logs for Linux Dedicated/EP Skus. (#10684)
- Allow sync trigger to happen in managed environment when `AzureWebJobsStorage` is not set (#10767)
- Fixing default DateTime bug with TimeZones in TimerTrigger (#10906)
- Adjusting the logic to determine the warmup call in placeholder simulation mode to align with the production flow (#10918)
- Fixing invalid DateTimes in status blobs when invoking via portal (#10916)
- Bug fix for platform release channel bundles resolution casing issue and additional logging (#10921)
- Adding support for faas.invoke_duration metric and other spec related updates (#10929)
- Fixed bug that could result in "Binding names must be unique" error (#10938)
