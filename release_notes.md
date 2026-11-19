### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update PS 7.6 worker to [v4.0.5641](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5641) (#12197)
- Flex Consumption: send the SHA-256 hash of the sync triggers payload with the `update-triggers` platform notification, and notify on every non-background sync regardless of the publish result. Notification now requires both `FUNCTIONS_NOTIFY_PLATFORM_ON_SYNC` and a Flex Consumption app.
