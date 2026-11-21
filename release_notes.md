### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update PS 7.6 worker to [v4.0.5641](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5641) (#12197)
- Flex Consumption: new `FUNCTIONS_FLEX_SYNC_TRIGGERS_STORE` setting. When set, the host notifies the platform (`update-triggers`) on every foreground sync, in parallel with the publish, and an explicit `synctriggers` returns 500 when the platform reports the store failed, doesn't answer in time or doesn't confirm it (a deliberate skip is success). Replaces the unused `FUNCTIONS_NOTIFY_PLATFORM_ON_SYNC` setting, which is removed. (#12201)
- Replace NUL characters in system log details, summaries, and exception messages with `<NUL>` to prevent field-shifted rows in log ingestion (#12199)