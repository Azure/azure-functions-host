### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update PS 7.6 worker to [v4.0.5641](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5641) (#12197)
- Flex Consumption: notify the platform (`update-triggers`) on every non-background sync, before and independent of the publish result; the background sync and the deployment validation worker do not notify. Notification now requires both `FUNCTIONS_NOTIFY_PLATFORM_ON_SYNC` and a Flex Consumption app. (#12201)
