### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update PS 7.6 worker to [v4.0.5641](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5641) (#12197)
- Flex Consumption: notify the platform (`update-triggers`) on every foreground sync, in parallel with and independent of the publish; the background sync does not notify. When the platform reports that it failed to store the triggers payload (or times out), the sync fails, so an explicit `synctriggers` returns 500. Notification now requires both `FUNCTIONS_NOTIFY_PLATFORM_ON_SYNC` and a Flex Consumption app. (#12201)
- Replace NUL characters in system log details, summaries, and exception messages with `<NUL>` to prevent field-shifted rows in log ingestion (#12199)